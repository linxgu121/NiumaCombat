using System;
using System.Collections.Generic;
using NiumaCombat.Data;
using NiumaCombat.Hitbox;
using UnityEngine;

namespace NiumaCombat.Service
{
    public sealed class CombatHitboxService : ICombatHitboxService
    {
        private readonly Dictionary<string, CombatHitboxRuntimeState> _activeHitboxes = new Dictionary<string, CombatHitboxRuntimeState>();
        private readonly ICombatCommand _combatCommand;
        private readonly Func<float> _timeProvider;

        public CombatHitboxService(ICombatCommand combatCommand, Func<float> timeProvider = null)
        {
            _combatCommand = combatCommand;
            _timeProvider = timeProvider;
        }

        public string OpenHitbox(CombatHitboxDefinition definition, string ownerActorId)
        {
            if (definition == null || string.IsNullOrWhiteSpace(ownerActorId))
            {
                return null;
            }

            var instanceId = Guid.NewGuid().ToString("N");
            _activeHitboxes[instanceId] = new CombatHitboxRuntimeState
            {
                AttackInstanceId = instanceId,
                OwnerActorId = ownerActorId,
                Definition = definition.Clone(),
                RemainingSeconds = definition.ActiveSeconds,
                OpenedAtTime = GetTime(),
                IsActive = true,
                EffectiveHitCount = 0
            };

            return instanceId;
        }

        public bool CloseHitbox(string attackInstanceId)
        {
            if (string.IsNullOrWhiteSpace(attackInstanceId))
            {
                return false;
            }

            var removed = _activeHitboxes.Remove(attackInstanceId);
            _combatCommand?.ClearAttackHits(attackInstanceId);
            return removed;
        }

        public bool IsHitboxActive(string attackInstanceId)
        {
            return !string.IsNullOrWhiteSpace(attackInstanceId)
                && _activeHitboxes.TryGetValue(attackInstanceId, out var state)
                && state != null
                && state.IsActive;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _activeHitboxes.Count == 0)
            {
                return;
            }

            List<string> expiredIds = null;
            foreach (var pair in _activeHitboxes)
            {
                var state = pair.Value;
                if (state == null || state.Definition == null || state.Definition.ActiveSeconds <= 0f)
                {
                    continue;
                }

                state.RemainingSeconds -= deltaTime;
                if (state.RemainingSeconds <= 0f)
                {
                    expiredIds ??= new List<string>();
                    expiredIds.Add(pair.Key);
                }
            }

            if (expiredIds == null)
            {
                return;
            }

            for (var i = 0; i < expiredIds.Count; i++)
            {
                CloseHitbox(expiredIds[i]);
            }
        }

        public bool TryGetState(string attackInstanceId, out CombatHitboxRuntimeState state)
        {
            if (string.IsNullOrWhiteSpace(attackInstanceId))
            {
                state = null;
                return false;
            }

            return _activeHitboxes.TryGetValue(attackInstanceId, out state) && state != null;
        }

        public bool TryIncrementHitCount(string attackInstanceId)
        {
            if (!TryGetState(attackInstanceId, out var state) || state.Definition == null)
            {
                return true;
            }

            if (state.Definition.MaxHitCount > 0 && state.EffectiveHitCount >= state.Definition.MaxHitCount)
            {
                return false;
            }

            state.EffectiveHitCount++;
            return true;
        }

        public bool HasReachedMaxHitCount(string attackInstanceId)
        {
            if (!TryGetState(attackInstanceId, out var state) || state.Definition == null || state.Definition.MaxHitCount <= 0)
            {
                return false;
            }

            return state.EffectiveHitCount >= state.Definition.MaxHitCount;
        }

        private float GetTime()
        {
            return _timeProvider != null ? _timeProvider() : Time.time;
        }
    }
}