using System;
using System.Collections.Generic;
using NiumaAttribute.Result;
using NiumaAttribute.Service;
using NiumaCombat.Data;
using NiumaCombat.Enum;
using NiumaCombat.Event;
using NiumaCore.Event;
using UnityEngine;

namespace NiumaCombat.Service
{
    public sealed class CombatService : ICombatService, ICombatConfigurationService
    {
        private const string SourceModuleName = "NiumaCombat";

        private readonly CombatDamageCalculator _calculator;
        private readonly Dictionary<string, CombatHitRecord> _hitRecords = new Dictionary<string, CombatHitRecord>();
        private readonly Queue<CombatResult> _resultCache = new Queue<CombatResult>();
        private readonly Dictionary<string, Queue<CombatResult>> _outgoingResults = new Dictionary<string, Queue<CombatResult>>();
        private readonly Dictionary<string, Queue<CombatResult>> _incomingResults = new Dictionary<string, Queue<CombatResult>>();
        private readonly int _maxGlobalResults;
        private readonly int _maxActorResults;
        private readonly string _hpResourceId;

        private IAttributeQuery _attributeQuery;
        private IAttributeCommand _attributeCommand;
        private ICombatFactionResolver _factionResolver;
        private IEventBus _eventBus;
        private ICombatHitboxService _hitboxService;
        private ICombatHitboxRuntimeAccess _hitboxRuntimeAccess;

        private sealed class HitRecordReservation
        {
            public bool Succeeded;
            public bool IncrementedHitCount;
            public bool HadPreviousRecord;
            public string Key;
            public CombatHitRecord PreviousRecord;
            public CombatHitRecord CurrentRecord;
            public CombatFailureReason FailureReason;
        }

        public CombatService(
            IAttributeQuery attributeQuery = null,
            IAttributeCommand attributeCommand = null,
            ICombatFactionResolver factionResolver = null,
            IEventBus eventBus = null,
            string hpResourceId = "hp",
            int maxGlobalResults = 128,
            int maxActorResults = 32,
            CombatDamageCalculator calculator = null)
        {
            _attributeQuery = attributeQuery;
            _attributeCommand = attributeCommand;
            _factionResolver = factionResolver;
            _eventBus = eventBus;
            _hpResourceId = string.IsNullOrWhiteSpace(hpResourceId) ? "hp" : hpResourceId.Trim();
            _maxGlobalResults = Mathf.Max(1, maxGlobalResults);
            _maxActorResults = Mathf.Max(1, maxActorResults);
            _calculator = calculator ?? new CombatDamageCalculator();
        }

        public long Revision { get; private set; }

        public void SetHitboxService(ICombatHitboxService hitboxService)
        {
            _hitboxService = hitboxService;
            _hitboxRuntimeAccess = hitboxService as ICombatHitboxRuntimeAccess;
        }

        public bool IsActorAlive(string actorId)
        {
            if (_attributeQuery == null || string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            return _attributeQuery.GetResourceCurrent(actorId, _hpResourceId, 0f) > 0f;
        }

        public bool CanTarget(string sourceActorId, string targetActorId)
        {
            if (string.IsNullOrWhiteSpace(sourceActorId) || string.IsNullOrWhiteSpace(targetActorId))
            {
                return false;
            }

            if (string.Equals(sourceActorId, targetActorId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsActorAlive(sourceActorId))
            {
                return false;
            }

            if (!IsActorAlive(targetActorId))
            {
                return false;
            }

            if (_factionResolver == null)
            {
                return true;
            }

            return _factionResolver.GetRelation(sourceActorId, targetActorId) == CombatTeamRelation.Hostile;
        }

        public bool HasHit(string attackInstanceId, string targetActorId)
        {
            return _hitRecords.ContainsKey(BuildHitKey(attackInstanceId, targetActorId));
        }

        public CombatResult GetLastOutgoingResult(string sourceActorId)
        {
            return GetLastFromMap(_outgoingResults, sourceActorId);
        }

        public CombatResult GetLastIncomingResult(string targetActorId)
        {
            return GetLastFromMap(_incomingResults, targetActorId);
        }

        public CombatResult[] GetRecentResults(string actorId, CombatResultActorRole role, int maxCount = 16)
        {
            if (string.IsNullOrWhiteSpace(actorId) || maxCount <= 0)
            {
                return Array.Empty<CombatResult>();
            }

            var limit = Mathf.Min(maxCount, _maxActorResults);
            if (role == CombatResultActorRole.Source)
            {
                return CopyRecent(_outgoingResults, actorId, limit);
            }

            if (role == CombatResultActorRole.Target)
            {
                return CopyRecent(_incomingResults, actorId, limit);
            }

            var merged = new List<CombatResult>(limit * 2);
            merged.AddRange(CopyRecent(_outgoingResults, actorId, limit));
            merged.AddRange(CopyRecent(_incomingResults, actorId, limit));
            merged.Sort((left, right) => right.ResolvedAtUnixMs.CompareTo(left.ResolvedAtUnixMs));
            if (merged.Count > limit)
            {
                merged.RemoveRange(limit, merged.Count - limit);
            }

            for (var i = 0; i < merged.Count; i++)
            {
                merged[i] = merged[i]?.Clone();
            }

            return merged.ToArray();
        }

        public void SetAttributeService(IAttributeQuery query, IAttributeCommand command)
        {
            _attributeQuery = query;
            _attributeCommand = command;
        }

        public void SetFactionResolver(ICombatFactionResolver resolver)
        {
            _factionResolver = resolver;
        }

        public void SetEventBus(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Tick(float deltaTime)
        {
            _hitboxRuntimeAccess?.Tick(deltaTime);
        }

        public CombatResult ApplyDamage(CombatDamageRequest request)
        {
            if (request == null)
            {
                return CommitRejected(BuildFailure((CombatDamageRequest)null, CombatFailureReason.InvalidRequest, "Damage request is null."));
            }

            if (!ValidateAttributeDependencies(out var dependencyFailure))
            {
                return CommitRejected(BuildFailure(request, dependencyFailure, "Attribute service is missing."));
            }

            if (string.IsNullOrWhiteSpace(request.SourceActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.SourceActorMissing, "SourceActorId is empty."));
            }

            if (string.IsNullOrWhiteSpace(request.TargetActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.TargetActorMissing, "TargetActorId is empty."));
            }

            if (!IsActorAlive(request.SourceActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.SourceDead, "Source actor is dead."));
            }

            if (string.Equals(request.SourceActorId, request.TargetActorId, StringComparison.Ordinal))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.SelfTargetRejected, "Self target is not allowed."));
            }

            if (!IsActorAlive(request.TargetActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.TargetDead, "Target is dead."));
            }

            if (_factionResolver != null)
            {
                var relation = ResolveRelation(request.SourceActorId, request.TargetActorId);
                if (relation == CombatTeamRelation.Unknown)
                {
                    return CommitRejected(BuildFailure(request, CombatFailureReason.FactionUnknown, "Faction relation is unknown."));
                }

                if (relation != CombatTeamRelation.Hostile)
                {
                    return CommitRejected(BuildFailure(request, CombatFailureReason.FactionRejected, "Target is not hostile."));
                }
            }

            if (!ValidateHitboxTarget(request, out var targetFailure))
            {
                return CommitRejected(BuildFailure(request, targetFailure, "Hitbox target validation failed."));
            }

            HitRecordReservation hitReservation = null;
            if (!string.IsNullOrWhiteSpace(request.AttackInstanceId))
            {
                var record = new CombatHitRecord
                {
                    AttackInstanceId = request.AttackInstanceId,
                    TargetActorId = request.TargetActorId,
                    HurtboxId = request.HurtboxId,
                    HitTime = Time.time
                };

                if (!CanRegisterHit(record, out var hitFailureReason))
                {
                    return CommitRejected(BuildFailure(request, hitFailureReason, "Duplicate hit or max hit count reached."));
                }

                hitReservation = ReserveHitRecord(record);
                if (!hitReservation.Succeeded)
                {
                    return CommitRejected(BuildFailure(request, hitReservation.FailureReason, "Duplicate hit or max hit count reached."));
                }
            }

            var hpBefore = _attributeQuery.GetResourceCurrent(request.TargetActorId, _hpResourceId, 0f);
            var calculation = _calculator.CalculateDamage(request, _attributeQuery);
            var confirmedResult = BuildResultFromDamage(request, calculation, hpBefore, hpBefore);
            PublishHitConfirmed(confirmedResult);

            if (calculation.FinalValue > 0f)
            {
                var operation = _attributeCommand.ConsumeResource(request.TargetActorId, _hpResourceId, calculation.FinalValue, SourceModuleName);
                if (!IsAttributeOperationSucceeded(operation))
                {
                    RollbackHitReservation(hitReservation);
                    return CommitRejected(BuildFailure(request, CombatFailureReason.AttributeWriteFailed, operation != null ? operation.Message : "Attribute write failed."));
                }
            }

            var hpAfter = _attributeQuery.GetResourceCurrent(request.TargetActorId, _hpResourceId, 0f);
            var result = BuildResultFromDamage(request, calculation, hpBefore, hpAfter);
            CommitResult(result);
            PublishResolvedEvents(result);
            return result.Clone();
        }

        public CombatResult ApplyHeal(CombatHealRequest request)
        {
            if (request == null)
            {
                return CommitRejected(BuildFailure((CombatHealRequest)null, CombatFailureReason.InvalidRequest, "Heal request is null."));
            }

            if (!ValidateAttributeDependencies(out var dependencyFailure))
            {
                return CommitRejected(BuildFailure(request, dependencyFailure, "Attribute service is missing."));
            }

            if (string.IsNullOrWhiteSpace(request.TargetActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.TargetActorMissing, "TargetActorId is empty."));
            }

            if (!IsActorAlive(request.TargetActorId))
            {
                return CommitRejected(BuildFailure(request, CombatFailureReason.TargetDead, "Target is dead."));
            }

            var hpBefore = _attributeQuery.GetResourceCurrent(request.TargetActorId, _hpResourceId, 0f);
            var calculation = _calculator.CalculateHeal(request, _attributeQuery);
            if (calculation.FinalValue > 0f)
            {
                var operation = _attributeCommand.RecoverResource(request.TargetActorId, _hpResourceId, calculation.FinalValue, SourceModuleName);
                if (!IsAttributeOperationSucceeded(operation))
                {
                    return CommitRejected(BuildFailure(request, CombatFailureReason.AttributeWriteFailed, operation != null ? operation.Message : "Attribute write failed."));
                }
            }

            var hpAfter = _attributeQuery.GetResourceCurrent(request.TargetActorId, _hpResourceId, 0f);
            var result = new CombatResult
            {
                RequestId = request.RequestId,
                SourceActorId = request.SourceActorId,
                TargetActorId = request.TargetActorId,
                SkillId = request.SkillId,
                ResultType = CombatResultType.Heal,
                RawValue = calculation.RawValue,
                FinalValue = calculation.FinalValue,
                TargetHpBefore = hpBefore,
                TargetHpAfter = hpAfter,
                FailureReason = CombatFailureReason.None,
                HitPoint = request.HitPoint,
                ResolvedAtUnixMs = NowMs()
            };

            CommitResult(result);
            PublishResolvedEvents(result);
            return result.Clone();
        }

        public bool TryRegisterHit(CombatHitRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.AttackInstanceId) || string.IsNullOrWhiteSpace(record.TargetActorId))
            {
                return false;
            }

            if (!CanRegisterHit(record, out _))
            {
                return false;
            }

            return ReserveHitRecord(record).Succeeded;
        }

        private bool CanRegisterHit(CombatHitRecord record, out CombatFailureReason failureReason)
        {
            failureReason = CombatFailureReason.None;
            if (record == null || string.IsNullOrWhiteSpace(record.AttackInstanceId) || string.IsNullOrWhiteSpace(record.TargetActorId))
            {
                failureReason = CombatFailureReason.InvalidRequest;
                return false;
            }

            var key = BuildHitKey(record.AttackInstanceId, record.TargetActorId);
            var hasExistingHit = _hitRecords.TryGetValue(key, out var existingRecord);
            if (_hitboxRuntimeAccess == null)
            {
                failureReason = CombatFailureReason.InternalError;
                return false;
            }

            if (!_hitboxRuntimeAccess.IsHitboxActive(record.AttackInstanceId))
            {
                failureReason = CombatFailureReason.HitboxNotActive;
                return false;
            }

            var definition = TryGetActiveHitboxDefinition(record.AttackInstanceId, out var activeDefinition) ? activeDefinition : null;

            if (!hasExistingHit)
            {
                return true;
            }

            if (definition == null || definition.HitSameTargetOnce)
            {
                failureReason = CombatFailureReason.DuplicateHit;
                return false;
            }

            if (definition.SameTargetHitCooldownSeconds > 0f && record.HitTime - existingRecord.HitTime < definition.SameTargetHitCooldownSeconds)
            {
                failureReason = CombatFailureReason.DuplicateHit;
                return false;
            }

            return true;
        }

        private HitRecordReservation ReserveHitRecord(CombatHitRecord record)
        {
            var reservation = new HitRecordReservation
            {
                Succeeded = false,
                FailureReason = CombatFailureReason.InvalidRequest
            };

            if (record == null || string.IsNullOrWhiteSpace(record.AttackInstanceId) || string.IsNullOrWhiteSpace(record.TargetActorId))
            {
                return reservation;
            }

            reservation.Key = BuildHitKey(record.AttackInstanceId, record.TargetActorId);
            reservation.HadPreviousRecord = _hitRecords.TryGetValue(reservation.Key, out var previousRecord);
            reservation.PreviousRecord = previousRecord?.Clone();
            reservation.CurrentRecord = record.Clone();

            if (_hitboxRuntimeAccess != null)
            {
                if (!_hitboxRuntimeAccess.TryIncrementHitCount(record.AttackInstanceId))
                {
                    reservation.FailureReason = CombatFailureReason.MaxHitCountReached;
                    return reservation;
                }

                reservation.IncrementedHitCount = true;
            }

            _hitRecords[reservation.Key] = record.Clone();
            reservation.Succeeded = true;
            reservation.FailureReason = CombatFailureReason.None;
            return reservation;
        }

        private void RollbackHitReservation(HitRecordReservation reservation)
        {
            if (reservation == null || !reservation.Succeeded)
            {
                return;
            }

            if (reservation.IncrementedHitCount && reservation.CurrentRecord != null)
            {
                _hitboxRuntimeAccess?.TryDecrementHitCount(reservation.CurrentRecord.AttackInstanceId);
            }

            if (string.IsNullOrWhiteSpace(reservation.Key))
            {
                return;
            }

            if (reservation.HadPreviousRecord && reservation.PreviousRecord != null)
            {
                _hitRecords[reservation.Key] = reservation.PreviousRecord.Clone();
            }
            else
            {
                _hitRecords.Remove(reservation.Key);
            }
        }

        public void ClearAttackHits(string attackInstanceId)
        {
            if (string.IsNullOrWhiteSpace(attackInstanceId) || _hitRecords.Count == 0)
            {
                return;
            }

            List<string> removeKeys = null;
            foreach (var pair in _hitRecords)
            {
                if (!string.Equals(pair.Value?.AttackInstanceId, attackInstanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                removeKeys ??= new List<string>();
                removeKeys.Add(pair.Key);
            }

            if (removeKeys == null)
            {
                return;
            }

            for (var i = 0; i < removeKeys.Count; i++)
            {
                _hitRecords.Remove(removeKeys[i]);
            }
        }

        private bool ValidateHitboxTarget(CombatDamageRequest request, out CombatFailureReason failureReason)
        {
            if (request == null)
            {
                failureReason = CombatFailureReason.InvalidRequest;
                return false;
            }

            CombatHitboxDefinition definition = null;
            if (!string.IsNullOrWhiteSpace(request.AttackInstanceId))
            {
                if (_hitboxRuntimeAccess == null)
                {
                    failureReason = CombatFailureReason.InternalError;
                    return false;
                }

                if (!_hitboxRuntimeAccess.IsHitboxActive(request.AttackInstanceId))
                {
                    failureReason = CombatFailureReason.HitboxNotActive;
                    return false;
                }

                if (!_hitboxRuntimeAccess.TryGetState(request.AttackInstanceId, out var state) || state == null)
                {
                    failureReason = CombatFailureReason.HitboxNotActive;
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(state.OwnerActorId)
                    && !string.Equals(state.OwnerActorId, request.SourceActorId, StringComparison.Ordinal))
                {
                    failureReason = CombatFailureReason.InvalidRequest;
                    return false;
                }

                definition = state.Definition;
            }

            if (RequiresHurtbox(request) && string.IsNullOrWhiteSpace(request.HurtboxId))
            {
                failureReason = CombatFailureReason.InvalidRequest;
                return false;
            }

            if (definition != null)
            {
                if (!HasAllTags(request.TargetTags, definition.RequiredTargetTags))
                {
                    failureReason = CombatFailureReason.RequiredTagMissing;
                    return false;
                }

                if (HasAnyTag(request.TargetTags, definition.RejectedTargetTags))
                {
                    failureReason = CombatFailureReason.RejectedTagMatched;
                    return false;
                }
            }

            failureReason = CombatFailureReason.None;
            return true;
        }

        private bool TryGetActiveHitboxDefinition(string attackInstanceId, out CombatHitboxDefinition definition)
        {
            definition = null;
            if (_hitboxRuntimeAccess == null || string.IsNullOrWhiteSpace(attackInstanceId))
            {
                return false;
            }

            if (!_hitboxRuntimeAccess.TryGetState(attackInstanceId, out var state) || state == null || state.Definition == null)
            {
                return false;
            }

            definition = state.Definition;
            return true;
        }

        private static bool RequiresHurtbox(CombatDamageRequest request)
        {
            return request != null
                && !string.IsNullOrWhiteSpace(request.AttackInstanceId);
        }

        private static bool HasAllTags(string[] targetTags, string[] requiredTags)
        {
            if (requiredTags == null || requiredTags.Length == 0)
            {
                return true;
            }

            if (targetTags == null || targetTags.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < requiredTags.Length; i++)
            {
                var required = requiredTags[i];
                if (string.IsNullOrWhiteSpace(required))
                {
                    continue;
                }

                if (!ContainsTag(targetTags, required))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAnyTag(string[] targetTags, string[] rejectedTags)
        {
            if (targetTags == null || targetTags.Length == 0 || rejectedTags == null || rejectedTags.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < rejectedTags.Length; i++)
            {
                var rejected = rejectedTags[i];
                if (!string.IsNullOrWhiteSpace(rejected) && ContainsTag(targetTags, rejected))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTag(string[] tags, string expectedTag)
        {
            if (tags == null || string.IsNullOrWhiteSpace(expectedTag))
            {
                return false;
            }

            for (var i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], expectedTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private CombatTeamRelation ResolveRelation(string sourceActorId, string targetActorId)
        {
            if (_factionResolver == null)
            {
                return CombatTeamRelation.Unknown;
            }

            return _factionResolver.GetRelation(sourceActorId, targetActorId);
        }

        private bool ValidateAttributeDependencies(out CombatFailureReason failureReason)
        {
            if (_attributeQuery == null && _attributeCommand == null)
            {
                failureReason = CombatFailureReason.MissingAttributeService;
                return false;
            }

            if (_attributeQuery == null)
            {
                failureReason = CombatFailureReason.MissingAttributeQuery;
                return false;
            }

            if (_attributeCommand == null)
            {
                failureReason = CombatFailureReason.MissingAttributeCommand;
                return false;
            }

            failureReason = CombatFailureReason.None;
            return true;
        }

        private CombatResult BuildResultFromDamage(CombatDamageRequest request, CombatDamageCalculation calculation, float hpBefore, float hpAfter)
        {
            var result = new CombatResult
            {
                RequestId = request.RequestId,
                AttackInstanceId = request.AttackInstanceId,
                SourceActorId = request.SourceActorId,
                TargetActorId = request.TargetActorId,
                SkillId = request.SkillId,
                HitboxId = request.HitboxId,
                HurtboxId = request.HurtboxId,
                ResultType = CombatResultType.Damage,
                RawValue = calculation.RawValue,
                FinalValue = calculation.FinalValue,
                TargetHpBefore = hpBefore,
                TargetHpAfter = hpAfter,
                IsCritical = calculation.IsCritical,
                IsKilled = hpBefore > 0f && hpAfter <= 0f,
                FailureReason = CombatFailureReason.None,
                Reaction = ResolveReaction(request),
                HitPoint = request.HitPoint,
                ResolvedAtUnixMs = NowMs()
            };

            if (result.FinalValue <= 0f)
            {
                result.ResultType = CombatResultType.Blocked;
            }

            return result;
        }

        private CombatResult BuildFailure(CombatDamageRequest request, CombatFailureReason reason, string message)
        {
            return new CombatResult
            {
                RequestId = request?.RequestId,
                AttackInstanceId = request?.AttackInstanceId,
                SourceActorId = request?.SourceActorId,
                TargetActorId = request?.TargetActorId,
                SkillId = request?.SkillId,
                HitboxId = request?.HitboxId,
                HurtboxId = request?.HurtboxId,
                ResultType = ToFailureResultType(reason),
                FailureReason = reason,
                HitPoint = request != null ? request.HitPoint : Vector3.zero,
                ResolvedAtUnixMs = NowMs()
            };
        }

        private CombatResult BuildFailure(CombatHealRequest request, CombatFailureReason reason, string message)
        {
            return new CombatResult
            {
                RequestId = request?.RequestId,
                SourceActorId = request?.SourceActorId,
                TargetActorId = request?.TargetActorId,
                SkillId = request?.SkillId,
                ResultType = ToFailureResultType(reason),
                FailureReason = reason,
                HitPoint = request != null ? request.HitPoint : Vector3.zero,
                ResolvedAtUnixMs = NowMs()
            };
        }

        private CombatResult CommitRejected(CombatResult result)
        {
            if (result == null)
            {
                return null;
            }

            CommitResult(result);
            _eventBus?.Publish(new CombatResultRejectedEvent(result));
            return result.Clone();
        }

        private void CommitResult(CombatResult result)
        {
            if (result == null)
            {
                return;
            }

            Revision++;
            var snapshot = result.Clone();
            _resultCache.Enqueue(snapshot);
            while (_resultCache.Count > _maxGlobalResults)
            {
                _resultCache.Dequeue();
            }

            AddActorResult(_outgoingResults, snapshot.SourceActorId, snapshot);
            AddActorResult(_incomingResults, snapshot.TargetActorId, snapshot);
        }

        private void AddActorResult(Dictionary<string, Queue<CombatResult>> map, string actorId, CombatResult result)
        {
            if (string.IsNullOrWhiteSpace(actorId) || result == null)
            {
                return;
            }

            if (!map.TryGetValue(actorId, out var queue))
            {
                queue = new Queue<CombatResult>();
                map[actorId] = queue;
            }

            queue.Enqueue(result.Clone());
            while (queue.Count > _maxActorResults)
            {
                queue.Dequeue();
            }
        }

        private void PublishResolvedEvents(CombatResult result)
        {
            if (_eventBus == null || result == null)
            {
                return;
            }

            if (result.ResultType == CombatResultType.Damage || result.ResultType == CombatResultType.Blocked)
            {
                if (result.ResultType == CombatResultType.Damage)
                {
                    _eventBus.Publish(new CombatDamageAppliedEvent(result));
                }
            }
            else if (result.ResultType == CombatResultType.Heal && result.FinalValue > 0f)
            {
                _eventBus.Publish(new CombatHealedEvent(result));
            }

            if (result.IsKilled)
            {
                _eventBus.Publish(new CombatKilledEvent(result));
            }
        }

        private void PublishHitConfirmed(CombatResult result)
        {
            if (_eventBus == null || result == null)
            {
                return;
            }

            if (result.ResultType == CombatResultType.Damage || result.ResultType == CombatResultType.Blocked)
            {
                _eventBus.Publish(new CombatHitConfirmedEvent(result));
            }
        }

        private static CombatResultType ToFailureResultType(CombatFailureReason reason)
        {
            return IsFilteredFailure(reason) ? CombatResultType.Filtered : CombatResultType.Failed;
        }

        private static bool IsFilteredFailure(CombatFailureReason reason)
        {
            switch (reason)
            {
                case CombatFailureReason.DuplicateHit:
                case CombatFailureReason.MaxHitCountReached:
                case CombatFailureReason.HitboxNotActive:
                case CombatFailureReason.FactionRejected:
                case CombatFailureReason.FactionUnknown:
                case CombatFailureReason.SelfTargetRejected:
                case CombatFailureReason.TargetDead:
                case CombatFailureReason.SourceDead:
                case CombatFailureReason.RequiredTagMissing:
                case CombatFailureReason.RejectedTagMatched:
                    return true;
                default:
                    return false;
            }
        }

        private CombatResult GetLastFromMap(Dictionary<string, Queue<CombatResult>> map, string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId) || !map.TryGetValue(actorId, out var queue) || queue == null || queue.Count == 0)
            {
                return null;
            }

            CombatResult last = null;
            foreach (var item in queue)
            {
                last = item;
            }

            return last?.Clone();
        }

        private CombatResult[] CopyRecent(Dictionary<string, Queue<CombatResult>> map, string actorId, int maxCount)
        {
            if (string.IsNullOrWhiteSpace(actorId) || maxCount <= 0 || !map.TryGetValue(actorId, out var queue) || queue == null || queue.Count == 0)
            {
                return Array.Empty<CombatResult>();
            }

            var array = queue.ToArray();
            var count = Mathf.Min(maxCount, array.Length);
            var results = new CombatResult[count];
            for (var i = 0; i < count; i++)
            {
                results[i] = array[array.Length - 1 - i]?.Clone();
            }

            return results;
        }

        private static CombatHitReactionData ResolveReaction(CombatDamageRequest request)
        {
            if (request == null)
            {
                return null;
            }

            var reaction = request.Reaction?.Clone();
            if (reaction == null)
            {
                return null;
            }

            if (reaction.HitDirection == Vector3.zero && request.HitDirection != Vector3.zero)
            {
                reaction.HitDirection = request.HitDirection;
            }

            return reaction;
        }

        private static bool IsAttributeOperationSucceeded(AttributeOperationResult result)
        {
            return result != null && result.Succeeded;
        }

        private static string BuildHitKey(string attackInstanceId, string targetActorId)
        {
            if (string.IsNullOrWhiteSpace(attackInstanceId) || string.IsNullOrWhiteSpace(targetActorId))
            {
                return string.Empty;
            }

            return attackInstanceId.Trim() + ":" + targetActorId.Trim();
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
