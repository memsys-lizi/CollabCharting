using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal sealed class CollabMember
    {
        public string SteamId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsHost { get; set; }

        public bool IsLocal { get; set; }
    }

    internal sealed class CollabFriend
    {
        public string SteamId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;
    }

    internal sealed class CollabLock
    {
        public string Target { get; set; } = string.Empty;

        public string OwnerSteamId { get; set; } = string.Empty;

        public string OwnerName { get; set; } = string.Empty;

        public double ExpiresAtUnix { get; set; }
    }

    internal sealed class CollabStatus
    {
        public bool SteamAvailable { get; set; }

        public string LocalSteamId { get; set; } = string.Empty;

        public string LocalName { get; set; } = string.Empty;

        public bool InLobby { get; set; }

        public bool IsHost { get; set; }

        public string LobbyId { get; set; } = string.Empty;

        public string HostSteamId { get; set; } = string.Empty;

        public string LevelName { get; set; } = string.Empty;

        public string LevelPath { get; set; } = string.Empty;

        public int Revision { get; set; }

        public string SyncState { get; set; } = "idle";

        public float SyncProgress { get; set; }

        public string LastError { get; set; } = string.Empty;

        public List<CollabMember> Members { get; set; } = new List<CollabMember>();

        public List<CollabLock> Locks { get; set; } = new List<CollabLock>();

        public List<string> RecentEvents { get; set; } = new List<string>();
    }

    internal sealed class ResourceManifestEntry
    {
        public string RelativePath { get; set; } = string.Empty;

        public long Size { get; set; }

        public string Sha256 { get; set; } = string.Empty;
    }

    internal sealed class ResourceManifest
    {
        public string RootName { get; set; } = string.Empty;

        public string LevelRelativePath { get; set; } = string.Empty;

        public List<ResourceManifestEntry> Files { get; set; } = new List<ResourceManifestEntry>();
    }

    internal sealed class CollabSnapshot
    {
        public int Revision { get; set; }

        public string LevelText { get; set; } = string.Empty;

        public string BeforeLevelText { get; set; } = string.Empty;

        public string LevelRelativePath { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    internal sealed class CollabInitialLevel
    {
        public int Revision { get; set; }

        public string LevelText { get; set; } = string.Empty;

        public string LevelRelativePath { get; set; } = string.Empty;
    }

    internal sealed class CollabOperationBatch
    {
        public string OperationId { get; set; } = string.Empty;

        public int BaseRevision { get; set; }

        public int Revision { get; set; }

        public long ClientSeq { get; set; }

        public string AuthorSteamId { get; set; } = string.Empty;

        public string AuthorName { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public int SelectedFloor { get; set; } = -1;

        public List<CollabAtomicOperation> Ops { get; set; } = new List<CollabAtomicOperation>();

        public List<ResourceManifestEntry> RequiredFiles { get; set; } = new List<ResourceManifestEntry>();

        public bool Undone { get; set; }
    }

    internal sealed class CollabAtomicOperation
    {
        public string Kind { get; set; } = string.Empty;

        public CollabOperationTarget Target { get; set; } = new CollabOperationTarget();

        public string BeforeHash { get; set; } = string.Empty;

        public JObject Payload { get; set; } = new JObject();

        public JObject InversePayload { get; set; } = new JObject();
    }

    internal sealed class CollabOperationTarget
    {
        public string Domain { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public int Floor { get; set; } = -1;

        public string EventType { get; set; } = string.Empty;

        public int Index { get; set; } = -1;
    }

    internal sealed class CollabPropertyChange
    {
        public string Path { get; set; } = string.Empty;

        public bool OldExists { get; set; }

        public JToken? OldValue { get; set; }

        public bool NewExists { get; set; }

        public JToken? NewValue { get; set; }
    }

    internal sealed class CollabHistoryRequest
    {
        public bool Redo { get; set; }
    }

    internal sealed class CollabHistoryNotice
    {
        public bool Ok { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    internal sealed class CollabHistoryEntry
    {
        public string Id { get; set; } = string.Empty;

        public int Revision { get; set; }

        public string AuthorSteamId { get; set; } = string.Empty;

        public string AuthorName { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public List<JsonDiffOperation> Diff { get; set; } = new List<JsonDiffOperation>();

        public CollabOperationBatch? Batch { get; set; }

        public bool Undone { get; set; }
    }

    internal sealed class JsonDiffOperation
    {
        public string Path { get; set; } = string.Empty;

        public bool OldExists { get; set; }

        public JToken? OldValue { get; set; }

        public bool NewExists { get; set; }

        public JToken? NewValue { get; set; }
    }
}
