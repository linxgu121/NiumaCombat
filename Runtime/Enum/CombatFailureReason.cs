namespace NiumaCombat.Enum
{
    public enum CombatFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        MissingAttributeService = 2,
        MissingAttributeQuery = 3,
        MissingAttributeCommand = 4,
        SourceActorMissing = 5,
        TargetActorMissing = 6,
        TargetDead = 7,
        SelfTargetRejected = 8,
        FactionRejected = 9,
        FactionUnknown = 10,
        DuplicateHit = 11,
        MaxHitCountReached = 12,
        RequiredTagMissing = 13,
        RejectedTagMatched = 14,
        AttributeReadFailed = 15,
        AttributeWriteFailed = 16,
        HitboxNotActive = 17,
        DefinitionMissing = 18,
        InternalError = 19,
        SourceDead = 20
    }
}
