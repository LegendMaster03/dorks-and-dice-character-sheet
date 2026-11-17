namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Local irreversible outcome for an operation admitted by Site. Terminal
/// identities survive character-root deletion to prevent delayed replay after
/// a Site restore/recreation. This table is not Site authorization proof.
/// </summary>
public enum ProtectedMutationTerminalStatus { Committed = 1, Aborted = 2 }

public sealed class ProtectedMutationTerminal
{
    public Guid OperationId { get; set; }
    public Guid CharacterId { get; set; }
    public string OperationKind { get; set; } = string.Empty;
    public string CommandDigest { get; set; } = string.Empty;
    public string AdmissionDigest { get; set; } = string.Empty;
    public ProtectedMutationTerminalStatus Status { get; set; }
    public string? ReceiptEventId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
