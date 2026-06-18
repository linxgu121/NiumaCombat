using System;
using NiumaAttribute.Service;
using NiumaCombat.Data;
using NiumaCombat.Enum;
using UnityEngine;

namespace NiumaCombat.Service
{
    public readonly struct CombatDamageCalculation
    {
        public CombatDamageCalculation(float attackPart, float afterDamageMultiplier, float afterCritical, float defenseValue, float afterDefense, float resistanceValue, float afterResistance, float rawValue, float finalValue, bool isCritical)
        {
            AttackPart = attackPart;
            AfterDamageMultiplier = afterDamageMultiplier;
            AfterCritical = afterCritical;
            DefenseValue = defenseValue;
            AfterDefense = afterDefense;
            ResistanceValue = resistanceValue;
            AfterResistance = afterResistance;
            RawValue = rawValue;
            FinalValue = finalValue;
            IsCritical = isCritical;
        }

        public float AttackPart { get; }
        public float AfterDamageMultiplier { get; }
        public float AfterCritical { get; }
        public float DefenseValue { get; }
        public float AfterDefense { get; }
        public float ResistanceValue { get; }
        public float AfterResistance { get; }
        public float RawValue { get; }
        public float FinalValue { get; }
        public bool IsCritical { get; }
    }

    public readonly struct CombatHealCalculation
    {
        public CombatHealCalculation(float rawValue, float finalValue)
        {
            RawValue = rawValue;
            FinalValue = finalValue;
        }

        public float RawValue { get; }
        public float FinalValue { get; }
    }

    public sealed class CombatDamageCalculator
    {
        private const string PhysicalDefenseAttributeId = "defense_physical";
        private const string PhysicalResistanceAttributeId = "resistance_physical";
        private const string MagicalDefenseAttributeId = "defense_magical";
        private const string MagicalResistanceAttributeId = "resistance_magical";
        private const string CritRateAttributeId = "crit_rate";
        private const string CritDamageAttributeId = "crit_damage";
        private const float DefaultCritDamage = 2f;

        public CombatDamageCalculation CalculateDamage(CombatDamageRequest request, IAttributeQuery attributeQuery, bool forceCritical = false)
        {
            var attackAttribute = ReadAttribute(attributeQuery, request.SourceActorId, request.AttackAttributeId);
            var attackPart = request.BasePower + attackAttribute * request.AttackScale;
            var damageMultiplier = Mathf.Max(0f, request.DamageMultiplier);
            var afterDamageMultiplier = attackPart * damageMultiplier;

            var isCritical = forceCritical || ShouldCrit(request, attributeQuery);
            var critDamage = isCritical ? ResolveCritDamage(request, attributeQuery) : 1f;
            var afterCritical = afterDamageMultiplier * critDamage;

            var ignoreDefense = request.IgnoreDefense || request.DamageType == CombatDamageType.TrueDamage;
            var defenseValue = ignoreDefense ? 0f : ResolveDefense(request, attributeQuery);
            var afterDefense = ignoreDefense ? afterCritical : Mathf.Max(0f, afterCritical - defenseValue);

            var ignoreResistance = request.IgnoreResistance || request.DamageType == CombatDamageType.TrueDamage;
            var resistanceValue = ignoreResistance ? 0f : Mathf.Clamp01(ResolveResistance(request, attributeQuery));
            var afterResistance = ignoreResistance ? afterDefense : afterDefense * (1f - resistanceValue);

            var bodyPartMultiplier = Mathf.Max(0f, request.BodyPartMultiplier);
            var rawValue = afterResistance * bodyPartMultiplier;
            var finalValue = Mathf.Max(0f, rawValue);
            return new CombatDamageCalculation(attackPart, afterDamageMultiplier, afterCritical, defenseValue, afterDefense, resistanceValue, afterResistance, rawValue, finalValue, isCritical);
        }

        public CombatHealCalculation CalculateHeal(CombatHealRequest request, IAttributeQuery attributeQuery)
        {
            var healAttribute = ReadAttribute(attributeQuery, request.SourceActorId, request.HealPowerAttributeId);
            var healBaseValue = request.BasePower + healAttribute * request.HealScale;
            var healMultiplier = Mathf.Max(0f, request.HealMultiplier);
            var rawValue = healBaseValue * healMultiplier;
            var finalValue = Mathf.Max(0f, rawValue);
            return new CombatHealCalculation(rawValue, finalValue);
        }

        private static bool ShouldCrit(CombatDamageRequest request, IAttributeQuery attributeQuery)
        {
            if (!request.CanCrit)
            {
                return false;
            }

            var rate = request.CritRateOverride >= 0f
                ? request.CritRateOverride
                : ReadAttribute(attributeQuery, request.SourceActorId, CritRateAttributeId);

            return UnityEngine.Random.value < Mathf.Clamp01(rate);
        }

        private static float ResolveCritDamage(CombatDamageRequest request, IAttributeQuery attributeQuery)
        {
            if (request.CritDamageOverride > 0f)
            {
                return request.CritDamageOverride;
            }

            var attributeValue = ReadAttribute(attributeQuery, request.SourceActorId, CritDamageAttributeId);
            return attributeValue > 0f ? attributeValue : DefaultCritDamage;
        }

        private static float ResolveDefense(CombatDamageRequest request, IAttributeQuery attributeQuery)
        {
            var explicitValue = ReadAttribute(attributeQuery, request.TargetActorId, request.DefenseAttributeId);
            if (explicitValue > 0f || !string.IsNullOrWhiteSpace(request.DefenseAttributeId))
            {
                return Mathf.Max(0f, explicitValue * ResolveDefenseScale(request));
            }

            var fallbackId = request.DamageType == CombatDamageType.Magical ? MagicalDefenseAttributeId : PhysicalDefenseAttributeId;
            var fallback = ReadAttribute(attributeQuery, request.TargetActorId, fallbackId);
            return Mathf.Max(0f, fallback * ResolveDefenseScale(request));
        }

        private static float ResolveResistance(CombatDamageRequest request, IAttributeQuery attributeQuery)
        {
            var explicitValue = ReadAttribute(attributeQuery, request.TargetActorId, request.ResistanceAttributeId);
            if (explicitValue > 0f || !string.IsNullOrWhiteSpace(request.ResistanceAttributeId))
            {
                return explicitValue;
            }

            var fallbackId = request.DamageType == CombatDamageType.Magical ? MagicalResistanceAttributeId : PhysicalResistanceAttributeId;
            return ReadAttribute(attributeQuery, request.TargetActorId, fallbackId);
        }

        private static float ResolveDefenseScale(CombatDamageRequest request)
        {
            return Mathf.Max(0f, request.DefenseScale <= 0f ? 1f : request.DefenseScale);
        }

        private static float ReadAttribute(IAttributeQuery attributeQuery, string actorId, string attributeId)
        {
            if (attributeQuery == null || string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(attributeId))
            {
                return 0f;
            }

            return attributeQuery.GetFinalValue(actorId, attributeId, 0f);
        }
    }
}
