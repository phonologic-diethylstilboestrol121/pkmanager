namespace PkManager.Server.Models.Entity;

public class SaveFile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Generation { get; set; }
    public int? GameVersion { get; set; }
    public string? TrainerName { get; set; }
    public int? TrainerId { get; set; }
    public int? SecretId { get; set; }
    public int PlayTime { get; set; }
    public int BoxCount { get; set; }
    public int PokemonCount { get; set; }
    public bool IsValidSave { get; set; } = true;
    public byte[] RawSaveData { get; set; } = Array.Empty<byte>();
    public bool IsModified { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SaveBackupEntity
{
    public Guid Id { get; set; }
    public Guid SaveFileId { get; set; }
    public byte[] RawSaveData { get; set; } = Array.Empty<byte>();
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RomFileEntity
{
    public Guid Id { get; set; }
    public string GameId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Generation { get; set; }
    public byte[] RomData { get; set; } = Array.Empty<byte>();
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EmulatorSaveStateEntity
{
    public Guid Id { get; set; }
    public Guid SaveFileId { get; set; }
    public int Slot { get; set; }
    public byte[] StateData { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
}
