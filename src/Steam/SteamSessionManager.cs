using System;
using System.Collections.Generic;
using System.IO;
using ADOFAI;
using Newtonsoft.Json.Linq;
using Steamworks;

namespace CollabCharting
{
    internal sealed class SteamSessionManager : IDisposable
    {
        private const int MaxMembers = 8;
        private const string ProtocolVersion = "2";
        private readonly SteamTransport transport = new SteamTransport();
        private readonly List<string> recentEvents = new List<string>();
        private readonly Dictionary<string, CollabLock> locks = new Dictionary<string, CollabLock>();
        private readonly List<CollabOperationBatch> operationLog = new List<CollabOperationBatch>();
        private readonly Dictionary<string, CollabOperationBatch> pendingLocalOperations = new Dictionary<string, CollabOperationBatch>();
        private readonly List<PendingOperationProposal> pendingOperationProposals = new List<PendingOperationProposal>();
        private readonly SortedDictionary<int, CollabOperationBatch> pendingAcceptedOperations = new SortedDictionary<int, CollabOperationBatch>();
        private readonly Dictionary<string, long> lastClientSeqByAuthor = new Dictionary<string, long>();
        private readonly HashSet<ulong> initialSyncSent = new HashSet<ulong>();
        private Callback<LobbyChatUpdate_t>? lobbyChatUpdate;
        private Callback<GameLobbyJoinRequested_t>? joinRequested;
        private CallResult<LobbyCreated_t>? createLobbyResult;
        private CallResult<LobbyEnter_t>? joinLobbyResult;
        private CSteamID lobbyId;
        private int revision;
        private string lastError = string.Empty;
        private string syncState = "idle";
        private float syncProgress;
        private float lockHeartbeatTimer;
        private float pendingOperationResendTimer;
        private long nextClientSeq;
        private readonly List<CollabOperationBatch> pendingPlaybackOperations = new List<CollabOperationBatch>();
        private string pendingPlaybackReason = string.Empty;
        private string pendingPlaybackLevelPath = string.Empty;
        private bool pendingPlaybackNoticeShown;
        private bool waitingForEditor;

        public bool InLobby => lobbyId.IsValid();

        public bool IsHost => InLobby && SteamMatchmaking.GetLobbyOwner(lobbyId) == SteamUser.GetSteamID();

        public string LobbyId => InLobby ? lobbyId.m_SteamID.ToString() : string.Empty;

        public int Revision => revision;

        public IReadOnlyCollection<CollabLock> ActiveLocks => locks.Values;

        public bool IsWaitingForEditor => waitingForEditor;

        public void WarmupSteam()
        {
            try
            {
                if (!SteamIntegration.initialized)
                {
                    _ = SteamIntegration.instance;
                }

                if (SteamIntegration.initialized)
                {
                    SteamIntegration.instance.CheckCallbacks();
                }
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Steam warmup failed: {ex.Message}");
            }
        }

        public void Start()
        {
            WarmupSteam();
            if (!SteamIntegration.initialized)
            {
                return;
            }

            transport.Start();
            if (lobbyChatUpdate == null)
            {
                lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            }

            if (joinRequested == null)
            {
                joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            }
        }

        public CollabStatus GetStatus()
        {
            WarmupSteam();
            Start();
            bool steamAvailable = SteamIntegration.initialized;
            CSteamID localId = steamAvailable ? SteamUser.GetSteamID() : CSteamID.Nil;
            return new CollabStatus
            {
                SteamAvailable = steamAvailable,
                LocalSteamId = steamAvailable ? localId.m_SteamID.ToString() : string.Empty,
                LocalName = steamAvailable ? SteamFriends.GetPersonaName() : string.Empty,
                InLobby = InLobby,
                IsHost = steamAvailable && IsHost,
                LobbyId = LobbyId,
                HostSteamId = InLobby ? SteamMatchmaking.GetLobbyOwner(lobbyId).m_SteamID.ToString() : string.Empty,
                LevelName = EditorStateAdapter.CurrentLevelName,
                LevelPath = EditorStateAdapter.CurrentLevelPath,
                Revision = revision,
                SyncState = syncState,
                SyncProgress = syncProgress,
                LastError = lastError,
                Members = GetMembers(),
                Locks = new List<CollabLock>(locks.Values),
                RecentEvents = new List<string>(recentEvents)
            };
        }

        public object CreateLobby()
        {
            EnsureSteam();
            EnsureEditorLevel();
            if (InLobby)
            {
                LeaveLobby();
            }

            syncState = "creating";
            SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxMembers);
            createLobbyResult = CallResult<LobbyCreated_t>.Create();
            createLobbyResult.Set(call, OnLobbyCreated);
            AddEvent("正在创建协作房间。");
            EmitStatus();
            return GetStatus();
        }

        public object JoinLobby(string id)
        {
            EnsureSteam();
            if (!ADOBase.isLevelEditor && !waitingForEditor)
            {
                throw new InvalidOperationException("请先进入关卡编辑器，再通过“协作”面板加入房间。");
            }

            if (!ulong.TryParse(id, out ulong lobby))
            {
                throw new InvalidOperationException("无效的 Steam Lobby ID。");
            }

            syncState = "joining";
            SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobby));
            joinLobbyResult = CallResult<LobbyEnter_t>.Create();
            joinLobbyResult.Set(call, OnLobbyEntered);
            EmitStatus();
            return GetStatus();
        }

        public object LeaveLobby()
        {
            if (InLobby)
            {
                SteamMatchmaking.LeaveLobby(lobbyId);
            }

            lobbyId = CSteamID.Nil;
            revision = 0;
            locks.Clear();
            operationLog.Clear();
            pendingLocalOperations.Clear();
            pendingOperationProposals.Clear();
            pendingAcceptedOperations.Clear();
            lastClientSeqByAuthor.Clear();
            initialSyncSent.Clear();
            nextClientSeq = 0;
            EntityIdRegistry.Reset();
            ClearPendingPlaybackSync();
            waitingForEditor = false;
            syncState = "idle";
            syncProgress = 0f;
            AddEvent("已离开协作房间。");
            EmitStatus();
            return GetStatus();
        }

        public object InviteFriend(string steamId)
        {
            EnsureSteam();
            EnsureLobby();
            if (!ulong.TryParse(steamId, out ulong id))
            {
                throw new InvalidOperationException("无效的 Steam 好友 ID。");
            }

            bool ok = SteamMatchmaking.InviteUserToLobby(lobbyId, new CSteamID(id));
            AddEvent(ok ? "已发送 Steam 邀请。" : "Steam 邀请发送失败。");
            EmitStatus();
            return new { ok };
        }

        public object OpenInviteDialog()
        {
            EnsureSteam();
            EnsureLobby();
            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
            return new { ok = true };
        }

        public List<CollabFriend> GetFriends()
        {
            EnsureSteam();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            var friends = new List<CollabFriend>();
            for (int i = 0; i < count; i++)
            {
                CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                friends.Add(new CollabFriend
                {
                    SteamId = friend.m_SteamID.ToString(),
                    Name = SteamFriends.GetFriendPersonaName(friend),
                    State = SteamFriends.GetFriendPersonaState(friend).ToString().Replace("k_EPersonaState", string.Empty)
                });
            }

            return friends;
        }

        public object AcquireLock(string target)
        {
            EnsureLobby();
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException("锁定目标不能为空。");
            }

            if (TryGetRemoteLock(target, out CollabLock? remoteLock) && remoteLock != null)
            {
                if (ADOBase.editor != null)
                {
                    ADOBase.editor.ShowNotification($"{remoteLock.OwnerName} 正在编辑，当前对象只读");
                }

                AddEvent($"{target} 已被 {remoteLock.OwnerName} 锁定。");
                EmitStatus();
                return remoteLock;
            }

            var collabLock = new CollabLock
            {
                Target = target,
                OwnerSteamId = SteamUser.GetSteamID().m_SteamID.ToString(),
                OwnerName = SteamFriends.GetPersonaName(),
                ExpiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeSeconds()
            };
            ReleaseOtherLocalLocks(collabLock.OwnerSteamId, target);
            locks[target] = collabLock;
            Broadcast("lock.update", collabLock);
            EmitStatus();
            return collabLock;
        }

        public object ReleaseLock(string target)
        {
            EnsureLobby();
            locks.Remove(target);
            Broadcast("lock.release", new { target });
            EmitStatus();
            return new { ok = true };
        }

        public bool TryGetRemoteLock(string target, out CollabLock? collabLock)
        {
            collabLock = null;
            if (!locks.TryGetValue(target, out CollabLock existing))
            {
                return false;
            }

            string localId = SteamIntegration.initialized ? SteamUser.GetSteamID().m_SteamID.ToString() : string.Empty;
            if (string.Equals(existing.OwnerSteamId, localId, StringComparison.Ordinal))
            {
                return false;
            }

            collabLock = existing;
            return true;
        }

        public bool IsLocalLock(CollabLock collabLock)
        {
            if (collabLock == null || !SteamIntegration.initialized)
            {
                return false;
            }

            return string.Equals(
                collabLock.OwnerSteamId,
                SteamUser.GetSteamID().m_SteamID.ToString(),
                StringComparison.Ordinal);
        }

        public void Update(float dt)
        {
            if (!SteamIntegration.initialized)
            {
                return;
            }

            Start();
            SteamIntegration.instance.CheckCallbacks();
            foreach (SteamTransport.NetEnvelope envelope in transport.Poll())
            {
                HandleEnvelope(envelope);
            }

            PruneLocks();
            RefreshLocalLocks(dt);
            RetryPendingLocalOperations(dt);
            ApplyPendingPlaybackSyncIfReady();
        }

        public void PublishLocalOperationBatch(CollabOperationBatch batch)
        {
            if (!InLobby || batch == null || batch.Ops.Count == 0)
            {
                return;
            }

            string localId = SteamUser.GetSteamID().m_SteamID.ToString();
            string localName = SteamFriends.GetPersonaName();
            batch.AuthorSteamId = localId;
            batch.AuthorName = localName;
            batch.BaseRevision = revision;
            if (string.IsNullOrWhiteSpace(batch.OperationId))
            {
                batch.OperationId = Guid.NewGuid().ToString("N");
            }

            batch.ClientSeq = ++nextClientSeq;
            if (!AttachRequiredFiles(batch, out string resourceError))
            {
                if (ADOBase.editor != null)
                {
                    ADOBase.editor.ShowNotification(resourceError);
                }

                AddEvent(resourceError, localId, localName);
                EmitStatus();
                return;
            }

            if (IsHost)
            {
                revision++;
                batch.Revision = revision;
                RecordOperation(batch);
                BroadcastRequiredResources(batch);
                Broadcast("operation.accepted", batch);
                AddEvent($"{localName} 修改了谱面 r{revision}（{DescribeBatch(batch)}）。", localId, localName);
                Main.Mod?.Logger.Log($"Accepted local host operation r{revision} seq {batch.ClientSeq}: {DescribeBatch(batch)}");
                EmitStatus();
            }
            else
            {
                CSteamID host = SteamMatchmaking.GetLobbyOwner(lobbyId);
                pendingLocalOperations[batch.OperationId] = OperationDiffUtility.CloneBatch(batch);
                SendRequiredResources(host, batch);
                transport.Send(host, "operation.proposal", batch, revision);
                AddEvent($"已向房主提交本地操作（{DescribeBatch(batch)}）。", localId, localName);
                Main.Mod?.Logger.Log($"Sent operation proposal to host seq {batch.ClientSeq}: {DescribeBatch(batch)} / base r{revision}");
                EmitStatus();
            }
        }

        public void RequestCollaborativeUndoRedo(bool redo)
        {
            EnsureLobby();
            if (IsHost)
            {
                HandleHistoryRequest(SteamUser.GetSteamID().m_SteamID.ToString(), redo);
                return;
            }

            CSteamID host = SteamMatchmaking.GetLobbyOwner(lobbyId);
            transport.Send(host, "operation.undoRequest", new CollabHistoryRequest { Redo = redo }, revision);
            AddEvent(redo ? "已向房主请求协作 Redo。" : "已向房主请求协作 Undo。");
            EmitStatus();
        }

        public void Dispose()
        {
            transport.Dispose();
            lobbyChatUpdate?.Dispose();
            joinRequested?.Dispose();
            createLobbyResult?.Dispose();
            joinLobbyResult?.Dispose();
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Fail($"创建 Steam Lobby 失败：{result.m_eResult}");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            revision = 1;
            initialSyncSent.Clear();
            pendingOperationProposals.Clear();
            pendingAcceptedOperations.Clear();
            lastClientSeqByAuthor.Clear();
            nextClientSeq = 0;
            SteamMatchmaking.SetLobbyData(lobbyId, "modVersion", Main.Mod?.Info.Version ?? "unknown");
            SteamMatchmaking.SetLobbyData(lobbyId, "protocolVersion", ProtocolVersion);
            SteamMatchmaking.SetLobbyData(lobbyId, "hostSteamId", SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(lobbyId, "levelName", EditorStateAdapter.CurrentLevelName);
            syncState = "hosting";
            AddEvent("已创建协作房间。", SteamUser.GetSteamID().m_SteamID.ToString(), SteamFriends.GetPersonaName());
            OperationCapture.ResetBaseline();
            EntityIdRegistry.InitializeFromCurrentLevel();
            EmitStatus();
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Fail($"加入 Steam Lobby 失败：{result.m_EChatRoomEnterResponse}");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            string remoteProtocol = SteamMatchmaking.GetLobbyData(lobbyId, "protocolVersion");
            if (!string.IsNullOrWhiteSpace(remoteProtocol) &&
                !string.Equals(remoteProtocol, ProtocolVersion, StringComparison.Ordinal))
            {
                SteamMatchmaking.LeaveLobby(lobbyId);
                lobbyId = CSteamID.Nil;
                Fail($"协作协议不兼容：房间为 r{remoteProtocol}，当前 Mod 为 r{ProtocolVersion}。");
                return;
            }

            syncState = IsHost ? "hosting" : "syncing";
            AddEvent("已加入协作房间。", SteamUser.GetSteamID().m_SteamID.ToString(), SteamFriends.GetPersonaName());
            EmitStatus();
            if (IsHost)
            {
                return;
            }

            CSteamID host = SteamMatchmaking.GetLobbyOwner(lobbyId);
            transport.Send(host, "sync.request", new { }, revision);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            if (!InLobby || update.m_ulSteamIDLobby != lobbyId.m_SteamID)
            {
                return;
            }

            var changedUser = new CSteamID(update.m_ulSteamIDUserChanged);
            string changedId = changedUser.m_SteamID.ToString();
            string changedName = SteamFriends.GetFriendPersonaName(changedUser);
            var state = (EChatMemberStateChange)update.m_rgfChatMemberStateChange;
            bool left =
                (state & EChatMemberStateChange.k_EChatMemberStateChangeLeft) != 0 ||
                (state & EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) != 0 ||
                (state & EChatMemberStateChange.k_EChatMemberStateChangeKicked) != 0 ||
                (state & EChatMemberStateChange.k_EChatMemberStateChangeBanned) != 0;

            if ((state & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                AddEvent($"{changedName} 加入了协作房间。", changedId, changedName);
            }

            if (left)
            {
                RemoveLocksForOwner(changedId);
                pendingOperationProposals.RemoveAll(item => string.Equals(item.AuthorId, changedId, StringComparison.Ordinal));
                lastClientSeqByAuthor.Remove(changedId);
                AddEvent($"{changedName} 已离开协作房间。", changedId, changedName);

                string hostId = SteamMatchmaking.GetLobbyData(lobbyId, "hostSteamId");
                string localId = SteamUser.GetSteamID().m_SteamID.ToString();
                if (!string.IsNullOrWhiteSpace(hostId) &&
                    string.Equals(changedId, hostId, StringComparison.Ordinal) &&
                    !string.Equals(changedId, localId, StringComparison.Ordinal))
                {
                    if (ADOBase.editor != null)
                    {
                        ADOBase.editor.ShowNotification("房主已离开，协作结束");
                    }

                    LeaveLobby();
                    return;
                }
            }

            EmitStatus();
            if (!IsHost)
            {
                return;
            }
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            waitingForEditor = true;
            MainThreadDispatcher.Enqueue(EditorSceneNavigator.EnsureEditorScene);
            JoinLobby(request.m_steamIDLobby.m_SteamID.ToString());
        }

        private void HandleEnvelope(SteamTransport.NetEnvelope envelope)
        {
            if (envelope.Payload == null)
            {
                return;
            }

            try
            {
                switch (envelope.Type)
                {
                    case "sync.request":
                        if (IsHost && ulong.TryParse(envelope.Sender, out ulong peer))
                        {
                            SendInitialSync(new CSteamID(peer));
                        }
                        break;
                    case "resource.file":
                        HandleResourceFile(envelope.Payload);
                        break;
                    case "level.initial":
                        HandleInitialLevel(envelope.Payload);
                        break;
                    case "operation.accepted":
                        HandleOperationAccepted(envelope.Payload);
                        break;
                    case "operation.proposal":
                        HandleOperationProposal(envelope);
                        break;
                    case "operation.rejected":
                        HandleOperationRejected(envelope.Payload);
                        break;
                    case "lock.update":
                        HandleLockUpdate(envelope.Payload);
                        break;
                    case "lock.release":
                        HandleLockRelease(envelope.Payload);
                        break;
                    case "operation.undoRequest":
                        HandleHistoryRequestEnvelope(envelope);
                        break;
                    case "history.notice":
                        HandleHistoryNotice(envelope.Payload);
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        private void SendInitialSync(CSteamID peer)
        {
            if (!IsHost || !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            CSteamID local = SteamUser.GetSteamID();
            if (!peer.IsValid() || peer == local || initialSyncSent.Contains(peer.m_SteamID))
            {
                return;
            }

            initialSyncSent.Add(peer.m_SteamID);
            syncState = "sending";
            SendCurrentResources(peer);
            transport.Send(peer, "level.initial", new CollabInitialLevel
            {
                Revision = revision,
                LevelText = EditorStateAdapter.EncodeCurrentLevel(),
                LevelRelativePath = Path.GetFileName(EditorStateAdapter.CurrentLevelPath)
            }, revision);
            syncState = "hosting";
            syncProgress = 1f;
            AddEvent($"已向 {SteamFriends.GetFriendPersonaName(peer)} 发送完整关卡资源。", peer.m_SteamID.ToString(), SteamFriends.GetFriendPersonaName(peer));
            EmitStatus();
        }

        private void SendCurrentResources(CSteamID peer)
        {
            ResourceManifest manifest = ResourceSync.BuildManifest(EditorStateAdapter.CurrentLevelPath);
            int total = Math.Max(1, manifest.Files.Count);
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ResourceManifestEntry file = manifest.Files[i];
                transport.Send(peer, "resource.file", new
                {
                    manifest.LevelRelativePath,
                    file.RelativePath,
                    file.Size,
                    file.Sha256,
                    Base64 = ResourceSync.ReadFileBase64(EditorStateAdapter.CurrentLevelPath, file.RelativePath)
                }, revision);
                syncProgress = (i + 1) / (float)total;
            }
        }

        private void HandleResourceFile(JToken payload)
        {
            string relativePath = payload.Value<string>("RelativePath") ?? string.Empty;
            string base64 = payload.Value<string>("Base64") ?? string.Empty;
            string expectedSha256 = payload.Value<string>("Sha256") ?? string.Empty;
            long expectedSize = payload.Value<long?>("Size") ?? -1;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Rejected malformed collab resource {relativePath}: {ex.Message}");
                return;
            }

            string actualSha256 = ResourceSync.ComputeSha256(bytes);
            if ((expectedSize >= 0 && bytes.LongLength != expectedSize) ||
                (!string.IsNullOrWhiteSpace(expectedSha256) &&
                 !string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase)))
            {
                Main.Mod?.Logger.Warning($"Rejected collab resource {relativePath}: hash or size mismatch.");
                return;
            }

            if (IsHost && EditorStateAdapter.IsEditorReady)
            {
                ResourceSync.WriteFileBytesToLevelRoot(EditorStateAdapter.CurrentLevelPath, relativePath, bytes);
            }
            else
            {
                ResourceSync.WriteFileBytes(LobbyId, relativePath, bytes);
            }

            if (syncState != "synced")
            {
                syncState = "syncing";
            }

            DrainPendingOperationProposals();
            DrainAcceptedOperations();
            EmitStatus();
        }

        private void QueueOrApplyInitialLevel(string levelPath)
        {
            if (ADOBase.editor == null)
            {
                pendingPlaybackLevelPath = levelPath;
                pendingPlaybackReason = "initial-sync";
                waitingForEditor = true;
                MainThreadDispatcher.Enqueue(EditorSceneNavigator.EnsureEditorScene);
                NotifyPlaybackSyncQueued("正在进入编辑器，关卡同步将在编辑器加载后应用。");
                return;
            }

            if (EditorPlaybackState.IsPreviewPlaying)
            {
                pendingPlaybackLevelPath = levelPath;
                pendingPlaybackReason = "initial-sync";
                NotifyPlaybackSyncQueued("预览播放中，远端变更已排队，停止预览后自动应用。");
                return;
            }

            MainThreadDispatcher.Enqueue(() => EditorStateAdapter.LoadLevelFromCache(levelPath));
        }

        private void QueueOrApplyOperation(CollabOperationBatch batch, string reason)
        {
            if (ADOBase.editor == null)
            {
                pendingPlaybackOperations.Add(batch);
                pendingPlaybackReason = reason;
                pendingPlaybackLevelPath = string.Empty;
                waitingForEditor = true;
                MainThreadDispatcher.Enqueue(EditorSceneNavigator.EnsureEditorScene);
                NotifyPlaybackSyncQueued("正在进入编辑器，协作变更将在编辑器加载后应用。");
                return;
            }

            if (EditorPlaybackState.IsPreviewPlaying)
            {
                pendingPlaybackOperations.Add(batch);
                pendingPlaybackReason = reason;
                pendingPlaybackLevelPath = string.Empty;
                NotifyPlaybackSyncQueued("预览播放中，远端变更已排队，停止预览后自动应用。");
                return;
            }

            if (!OperationApplier.TryApply(batch, reason, out string conflict))
            {
                Fail($"应用协作操作失败：{conflict}");
            }
        }

        private void ApplyPendingPlaybackSyncIfReady()
        {
            if (EditorPlaybackState.IsPreviewPlaying ||
                ADOBase.editor == null ||
                (pendingPlaybackOperations.Count == 0 && string.IsNullOrWhiteSpace(pendingPlaybackLevelPath)))
            {
                return;
            }

            List<CollabOperationBatch> batches = new List<CollabOperationBatch>(pendingPlaybackOperations);
            string reason = pendingPlaybackReason;
            string levelPath = pendingPlaybackLevelPath;
            ClearPendingPlaybackSync();

            if (!string.IsNullOrWhiteSpace(levelPath))
            {
                MainThreadDispatcher.Enqueue(() => EditorStateAdapter.LoadLevelFromCache(levelPath));
                syncState = "synced";
                syncProgress = 1f;
                AddEvent("预览结束，已应用排队的初始同步。");
            }
            else if (batches.Count > 0)
            {
                foreach (CollabOperationBatch batch in batches)
                {
                    if (!OperationApplier.TryApply(batch, reason, out string conflict))
                    {
                        Fail($"应用排队协作操作失败：{conflict}");
                        return;
                    }
                }

                syncState = IsHost ? "hosting" : "synced";
                syncProgress = 1f;
                AddEvent("预览结束，已应用排队的协作变更。");
            }

            EmitStatus();
        }

        private void NotifyPlaybackSyncQueued(string message)
        {
            syncState = "queued";
            if (!pendingPlaybackNoticeShown)
            {
                pendingPlaybackNoticeShown = true;
                AddEvent(message);
                ADOBase.editor?.ShowNotification(message);
            }

            EmitStatus();
        }

        private void ClearPendingPlaybackSync()
        {
            pendingPlaybackOperations.Clear();
            pendingPlaybackReason = string.Empty;
            pendingPlaybackLevelPath = string.Empty;
            pendingPlaybackNoticeShown = false;
        }

        public void MarkEditorAvailable()
        {
            waitingForEditor = false;
        }

        private void HandleInitialLevel(JToken payload)
        {
            CollabInitialLevel initial = payload.ToObject<CollabInitialLevel>() ?? new CollabInitialLevel();
            revision = initial.Revision;
            operationLog.Clear();
            pendingLocalOperations.Clear();
            pendingOperationProposals.Clear();
            pendingAcceptedOperations.Clear();
            lastClientSeqByAuthor.Clear();
            nextClientSeq = 0;
            EntityIdRegistry.InitializeFromLevelText(initial.LevelText);
            string levelPath = ResourceSync.ResolveCachePath(LobbyId, initial.LevelRelativePath);
            File.WriteAllText(levelPath, initial.LevelText);
            syncState = "synced";
            syncProgress = 1f;
            AddEvent("初始关卡同步完成。");
            QueueOrApplyInitialLevel(levelPath);
            EmitStatus();
        }

        private void HandleOperationAccepted(JToken payload)
        {
            CollabOperationBatch batch = payload.ToObject<CollabOperationBatch>() ?? new CollabOperationBatch();
            if (batch.Revision <= revision || string.IsNullOrWhiteSpace(batch.OperationId))
            {
                return;
            }

            if (batch.Revision > revision + 1)
            {
                pendingAcceptedOperations[batch.Revision] = batch;
                AddEvent($"正在等待协作同步 r{revision + 1}，已暂存 r{batch.Revision}。");
                EmitStatus();
                return;
            }

            if (!HasRequiredResources(batch, out List<string> missingResources))
            {
                pendingAcceptedOperations[batch.Revision] = batch;
                AddEvent($"正在等待协作资源：{FormatMissingResources(missingResources)}。", batch.AuthorSteamId, batch.AuthorName);
                EmitStatus();
                return;
            }

            ApplyAcceptedOperation(batch);
            DrainAcceptedOperations();
        }

        private void ApplyAcceptedOperation(CollabOperationBatch batch)
        {
            bool ownPending = pendingLocalOperations.Remove(batch.OperationId);
            revision = batch.Revision;
            RecordOperation(batch);
            if (!ownPending)
            {
                QueueOrApplyOperation(batch, $"r{batch.Revision}");
            }

            AddEvent($"{batch.AuthorName} 的操作已同步 r{revision}（{DescribeBatch(batch)}）。", batch.AuthorSteamId, batch.AuthorName);
            EmitStatus();
        }

        private void DrainAcceptedOperations()
        {
            while (pendingAcceptedOperations.TryGetValue(revision + 1, out CollabOperationBatch batch))
            {
                if (!HasRequiredResources(batch, out _))
                {
                    return;
                }

                pendingAcceptedOperations.Remove(revision + 1);
                ApplyAcceptedOperation(batch);
            }
        }

        private void HandleOperationProposal(SteamTransport.NetEnvelope envelope)
        {
            if (!IsHost)
            {
                return;
            }

            JToken payload = envelope.Payload ?? new JObject();
            CollabOperationBatch batch = payload.ToObject<CollabOperationBatch>() ?? new CollabOperationBatch();
            string authorId = envelope.Sender;
            string authorName = ulong.TryParse(authorId, out ulong steamId)
                ? SteamFriends.GetFriendPersonaName(new CSteamID(steamId))
                : "成员";
            batch.AuthorSteamId = authorId;
            batch.AuthorName = string.IsNullOrWhiteSpace(batch.AuthorName) ? authorName : batch.AuthorName;
            Main.Mod?.Logger.Log($"Received operation proposal from {authorName} seq {batch.ClientSeq} ({ShortId(batch.OperationId)}): {DescribeBatch(batch)} / base r{batch.BaseRevision}");

            if (TryFindAcceptedOperation(batch.OperationId, out CollabOperationBatch? accepted) && accepted != null)
            {
                if (ulong.TryParse(authorId, out ulong duplicatePeer))
                {
                    transport.Send(new CSteamID(duplicatePeer), "operation.accepted", accepted, revision);
                    Main.Mod?.Logger.Log($"Replayed accepted operation {ShortId(batch.OperationId)} to {authorName}.");
                }

                return;
            }

            var proposal = new PendingOperationProposal
            {
                AuthorId = authorId,
                AuthorName = authorName,
                Batch = batch
            };

            if (!TryProcessOperationProposal(proposal, out string waitReason))
            {
                QueueOperationProposal(proposal, waitReason);
            }
            else
            {
                DrainPendingOperationProposals();
            }
        }

        private bool TryProcessOperationProposal(PendingOperationProposal proposal, out string waitReason)
        {
            waitReason = string.Empty;
            CollabOperationBatch batch = proposal.Batch;
            string authorId = proposal.AuthorId;
            string authorName = proposal.AuthorName;

            if (!HasRequiredResources(batch, out List<string> missingResources))
            {
                waitReason = $"等待资源：{FormatMissingResources(missingResources)}";
                return false;
            }

            if (!ValidateClientSequence(batch, authorId, out waitReason, out bool rejectedSequence))
            {
                return rejectedSequence;
            }

            CollabOperationBatch transformed = OperationDiffUtility.CloneBatch(batch);
            if (!OperationDiffUtility.TransformForAcceptedOperations(transformed, operationLog, out string transformConflict))
            {
                Main.Mod?.Logger.Warning($"Rejected operation transform from {authorName}: {transformConflict}");
                RejectOperation(authorId, transformed.OperationId, transformConflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            if (!ValidateLocks(transformed, authorId, out string lockConflict))
            {
                Main.Mod?.Logger.Warning($"Rejected operation lock from {authorName}: {lockConflict}");
                RejectOperation(authorId, transformed.OperationId, lockConflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            if (!OperationApplier.TryApply(transformed, $"client-change r{revision + 1}", out string conflict))
            {
                Main.Mod?.Logger.Warning($"Rejected operation apply from {authorName}: {conflict}");
                RejectOperation(authorId, transformed.OperationId, conflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            revision++;
            transformed.Revision = revision;
            transformed.ClientSeq = batch.ClientSeq;
            transformed.AuthorSteamId = authorId;
            transformed.AuthorName = authorName;
            MarkClientSequenceConsumed(authorId, batch.ClientSeq);

            RecordOperation(transformed);
            BroadcastRequiredResources(transformed, authorId);
            Broadcast("operation.accepted", transformed);
            AddEvent($"{authorName} 的操作已同步 r{revision}（{DescribeBatch(transformed)}）。", authorId, authorName);
            Main.Mod?.Logger.Log($"Accepted peer operation from {authorName} r{revision} seq {batch.ClientSeq}: {DescribeBatch(transformed)}");
            EmitStatus();
            return true;
        }

        private void QueueOperationProposal(PendingOperationProposal proposal, string reason)
        {
            if (pendingOperationProposals.Exists(item =>
                string.Equals(item.Batch.OperationId, proposal.Batch.OperationId, StringComparison.Ordinal)))
            {
                return;
            }

            pendingOperationProposals.Add(proposal);
            Main.Mod?.Logger.Log($"Queued operation proposal {ShortId(proposal.Batch.OperationId)} seq {proposal.Batch.ClientSeq} from {proposal.AuthorName}: {reason}");
            AddEvent($"{proposal.AuthorName} 的操作正在等待同步条件（{reason}）。", proposal.AuthorId, proposal.AuthorName);
            EmitStatus();
        }

        private void DrainPendingOperationProposals()
        {
            if (!IsHost || pendingOperationProposals.Count == 0)
            {
                return;
            }

            bool progressed;
            do
            {
                progressed = false;
                for (int i = 0; i < pendingOperationProposals.Count;)
                {
                    PendingOperationProposal proposal = pendingOperationProposals[i];
                    if (TryProcessOperationProposal(proposal, out _))
                    {
                        pendingOperationProposals.RemoveAt(i);
                        progressed = true;
                        continue;
                    }

                    i++;
                }
            }
            while (progressed);
        }

        private void RetryPendingLocalOperations(float dt)
        {
            if (!InLobby || IsHost || pendingLocalOperations.Count == 0)
            {
                pendingOperationResendTimer = 0f;
                return;
            }

            pendingOperationResendTimer += dt;
            if (pendingOperationResendTimer < 2.5f)
            {
                return;
            }

            pendingOperationResendTimer = 0f;
            CSteamID host = SteamMatchmaking.GetLobbyOwner(lobbyId);
            foreach (CollabOperationBatch batch in new List<CollabOperationBatch>(pendingLocalOperations.Values))
            {
                transport.Send(host, "operation.proposal", batch, revision);
                Main.Mod?.Logger.Log($"Retried operation proposal seq {batch.ClientSeq} ({ShortId(batch.OperationId)}) to host.");
            }
        }

        private void HandleOperationRejected(JToken payload)
        {
            string operationId = payload.Value<string>("operationId") ?? string.Empty;
            string reason = payload.Value<string>("reason") ?? "操作被房主拒绝";
            if (pendingLocalOperations.TryGetValue(operationId, out CollabOperationBatch pending))
            {
                pendingLocalOperations.Remove(operationId);
                CollabOperationBatch inverse = OperationDiffUtility.CreateInverse(pending, "revert-rejected-operation");
                if (!OperationApplier.TryApply(inverse, "撤回未确认操作", out string conflict))
                {
                    Main.Mod?.Logger.Warning($"Failed to revert rejected operation: {conflict}");
                }
            }

            if (ADOBase.editor != null)
            {
                ADOBase.editor.ShowNotification(reason);
            }

            AddEvent(reason);
            EmitStatus();
        }

        private void RejectOperation(string authorId, string operationId, string reason)
        {
            AddEvent($"已拒绝成员操作：{reason}", authorId);
            if (ulong.TryParse(authorId, out ulong peer))
            {
                transport.Send(new CSteamID(peer), "operation.rejected", new
                {
                    operationId,
                    reason
                }, revision);
            }

            EmitStatus();
        }

        private void HandleLockUpdate(JToken payload)
        {
            CollabLock? collabLock = payload.ToObject<CollabLock>();
            if (collabLock == null || string.IsNullOrWhiteSpace(collabLock.Target))
            {
                return;
            }

            locks[collabLock.Target] = collabLock;
            EmitStatus();
        }

        private void HandleLockRelease(JToken payload)
        {
            string target = payload.Value<string>("target") ?? string.Empty;
            locks.Remove(target);
            EmitStatus();
        }

        private bool ValidateLocks(CollabOperationBatch batch, string authorId, out string conflict)
        {
            conflict = string.Empty;
            foreach (CollabAtomicOperation op in batch.Ops)
            {
                foreach (string target in GetPossibleLockTargets(op))
                {
                    if (!locks.TryGetValue(target, out CollabLock collabLock))
                    {
                        continue;
                    }

                    if (!string.Equals(collabLock.OwnerSteamId, authorId, StringComparison.Ordinal))
                    {
                        conflict = $"{collabLock.OwnerName} 正在编辑该对象";
                        return false;
                    }
                }
            }

            return true;
        }

        private static IEnumerable<string> GetPossibleLockTargets(CollabAtomicOperation op)
        {
            if (op.Target.Domain == "event")
            {
                if (!string.IsNullOrWhiteSpace(op.Target.EntityId))
                {
                    yield return EditorLockTargets.EventIdPrefix + op.Target.EntityId;
                }

                yield return $"event:{op.Target.Floor}:{op.Target.EventType}:{op.Target.Index}";
                yield return $"floor:{op.Target.Floor}";
            }
            else if (op.Target.Domain == "decoration")
            {
                if (!string.IsNullOrWhiteSpace(op.Target.EntityId))
                {
                    yield return EditorLockTargets.DecorationIdPrefix + op.Target.EntityId;
                }

                yield return $"decoration:{op.Target.Index}";
            }
            else if (op.Target.Domain == "path" && op.Target.Index >= 0)
            {
                yield return $"floor:{op.Target.Index}";
            }
        }

        private static string DescribeBatch(CollabOperationBatch batch)
        {
            return OperationDiffUtility.DescribeBatch(batch);
        }

        private bool TryFindAcceptedOperation(string operationId, out CollabOperationBatch? accepted)
        {
            accepted = null;
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return false;
            }

            foreach (CollabOperationBatch batch in operationLog)
            {
                if (string.Equals(batch.OperationId, operationId, StringComparison.Ordinal))
                {
                    accepted = batch;
                    return true;
                }
            }

            return false;
        }

        private void MarkClientSequenceConsumed(string authorId, long clientSeq)
        {
            if (clientSeq <= 0 || string.IsNullOrWhiteSpace(authorId))
            {
                return;
            }

            lastClientSeqByAuthor.TryGetValue(authorId, out long lastSeq);
            if (clientSeq > lastSeq)
            {
                lastClientSeqByAuthor[authorId] = clientSeq;
            }
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "-";
            }

            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        private static bool AttachRequiredFiles(CollabOperationBatch batch, out string error)
        {
            error = string.Empty;
            if (!EditorStateAdapter.IsEditorReady)
            {
                return true;
            }

            if (!ResourceSync.TryCollectRequiredFiles(
                    batch,
                    EditorStateAdapter.CurrentLevelPath,
                    out List<ResourceManifestEntry> files,
                    out List<string> missingFiles))
            {
                error = $"操作引用的资源文件不存在：{FormatMissingResources(missingFiles)}";
                batch.RequiredFiles = files;
                return false;
            }

            batch.RequiredFiles = files;
            return true;
        }

        private bool HasRequiredResources(CollabOperationBatch batch, out List<string> missingResources)
        {
            missingResources = new List<string>();
            if (batch == null || batch.RequiredFiles.Count == 0)
            {
                return true;
            }

            if (IsHost && EditorStateAdapter.IsEditorReady)
            {
                return ResourceSync.LevelRootHasFiles(EditorStateAdapter.CurrentLevelPath, batch.RequiredFiles, out missingResources);
            }

            return ResourceSync.CacheHasFiles(LobbyId, batch.RequiredFiles, out missingResources);
        }

        private bool ValidateClientSequence(
            CollabOperationBatch batch,
            string authorId,
            out string waitReason,
            out bool rejected)
        {
            waitReason = string.Empty;
            rejected = false;
            if (batch.ClientSeq <= 0)
            {
                return true;
            }

            lastClientSeqByAuthor.TryGetValue(authorId, out long lastSeq);
            if (batch.ClientSeq <= lastSeq)
            {
                RejectOperation(authorId, batch.OperationId, $"操作序号已过期：{batch.ClientSeq}");
                rejected = true;
                return false;
            }

            if (batch.ClientSeq > lastSeq + 1)
            {
                waitReason = $"等待操作序号 {lastSeq + 1}";
                return false;
            }

            return true;
        }

        private static string FormatMissingResources(IList<string> missingFiles)
        {
            if (missingFiles == null || missingFiles.Count == 0)
            {
                return "未知文件";
            }

            if (missingFiles.Count <= 3)
            {
                return string.Join(", ", missingFiles);
            }

            return $"{string.Join(", ", new[] { missingFiles[0], missingFiles[1], missingFiles[2] })} 等 {missingFiles.Count} 个文件";
        }

        private void BroadcastRequiredResources(CollabOperationBatch batch, string exceptSteamId = "")
        {
            if (batch == null || batch.RequiredFiles.Count == 0)
            {
                return;
            }

            foreach (CollabMember member in GetMembers())
            {
                if (member.IsLocal ||
                    string.Equals(member.SteamId, exceptSteamId, StringComparison.Ordinal) ||
                    !ulong.TryParse(member.SteamId, out ulong id))
                {
                    continue;
                }

                SendRequiredResources(new CSteamID(id), batch);
            }
        }

        private void SendRequiredResources(CSteamID peer, CollabOperationBatch batch)
        {
            if (!peer.IsValid() ||
                batch == null ||
                batch.RequiredFiles.Count == 0 ||
                !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            foreach (ResourceManifestEntry file in batch.RequiredFiles)
            {
                try
                {
                    transport.Send(peer, "resource.file", new
                    {
                        LevelRelativePath = Path.GetFileName(EditorStateAdapter.CurrentLevelPath),
                        file.RelativePath,
                        file.Size,
                        file.Sha256,
                        Base64 = ResourceSync.ReadFileBase64(EditorStateAdapter.CurrentLevelPath, file.RelativePath)
                    }, revision);
                }
                catch (Exception ex)
                {
                    Main.Mod?.Logger.Warning($"Failed to send required collab resource {file.RelativePath}: {ex.Message}");
                }
            }
        }

        private void Broadcast(string type, object payload)
        {
            if (!InLobby)
            {
                return;
            }

            foreach (CollabMember member in GetMembers())
            {
                if (member.IsLocal || !ulong.TryParse(member.SteamId, out ulong id))
                {
                    continue;
                }

                transport.Send(new CSteamID(id), type, payload, revision);
            }
        }

        private List<CollabMember> GetMembers()
        {
            var members = new List<CollabMember>();
            if (!InLobby || !SteamIntegration.initialized)
            {
                return members;
            }

            CSteamID local = SteamUser.GetSteamID();
            CSteamID host = SteamMatchmaking.GetLobbyOwner(lobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                members.Add(new CollabMember
                {
                    SteamId = member.m_SteamID.ToString(),
                    Name = SteamFriends.GetFriendPersonaName(member),
                    IsHost = member == host,
                    IsLocal = member == local
                });
            }

            return members;
        }

        private void PruneLocks()
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expired = new List<string>();
            foreach (KeyValuePair<string, CollabLock> pair in locks)
            {
                if (pair.Value.ExpiresAtUnix <= now)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (string key in expired)
            {
                locks.Remove(key);
            }
        }

        private void RemoveLocksForOwner(string ownerSteamId)
        {
            if (string.IsNullOrWhiteSpace(ownerSteamId))
            {
                return;
            }

            var release = new List<string>();
            foreach (KeyValuePair<string, CollabLock> pair in locks)
            {
                if (string.Equals(pair.Value.OwnerSteamId, ownerSteamId, StringComparison.Ordinal))
                {
                    release.Add(pair.Key);
                }
            }

            foreach (string target in release)
            {
                locks.Remove(target);
            }
        }

        private void ReleaseOtherLocalLocks(string localId, string exceptTarget)
        {
            var release = new List<string>();
            foreach (KeyValuePair<string, CollabLock> pair in locks)
            {
                if (pair.Key != exceptTarget &&
                    string.Equals(pair.Value.OwnerSteamId, localId, StringComparison.Ordinal))
                {
                    release.Add(pair.Key);
                }
            }

            foreach (string target in release)
            {
                locks.Remove(target);
                Broadcast("lock.release", new { target });
            }
        }

        private void RefreshLocalLocks(float dt)
        {
            if (!InLobby)
            {
                return;
            }

            lockHeartbeatTimer += dt;
            if (lockHeartbeatTimer < 3f)
            {
                return;
            }

            lockHeartbeatTimer = 0f;
            string localId = SteamUser.GetSteamID().m_SteamID.ToString();
            bool changed = false;
            foreach (CollabLock collabLock in new List<CollabLock>(locks.Values))
            {
                if (!string.Equals(collabLock.OwnerSteamId, localId, StringComparison.Ordinal))
                {
                    continue;
                }

                collabLock.ExpiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeSeconds();
                Broadcast("lock.update", collabLock);
                changed = true;
            }

            if (changed)
            {
                EmitStatus();
            }
        }

        private void RecordOperation(CollabOperationBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.OperationId))
            {
                return;
            }

            operationLog.RemoveAll(existing => string.Equals(existing.OperationId, batch.OperationId, StringComparison.Ordinal));
            operationLog.Add(OperationDiffUtility.CloneBatch(batch));
            if (operationLog.Count > 256)
            {
                operationLog.RemoveAt(0);
            }
        }

        private void HandleHistoryRequestEnvelope(SteamTransport.NetEnvelope envelope)
        {
            if (!IsHost || envelope.Payload == null)
            {
                return;
            }

            CollabHistoryRequest request = envelope.Payload.ToObject<CollabHistoryRequest>() ?? new CollabHistoryRequest();
            HandleHistoryRequest(envelope.Sender, request.Redo);
        }

        private void HandleHistoryRequest(string authorSteamId, bool redo)
        {
            if (!IsHost)
            {
                return;
            }

            CollabOperationBatch? entry = FindHistoryEntry(authorSteamId, redo);
            if (entry == null)
            {
                SendHistoryNotice(authorSteamId, false, redo ? "没有可重做的个人操作。" : "没有可撤销的个人操作。");
                return;
            }

            CollabOperationBatch inverse = OperationDiffUtility.CreateInverse(entry, redo ? "collab-redo" : "collab-undo");
            inverse.BaseRevision = revision;
            if (!OperationApplier.TryApply(inverse, redo ? "协作 Redo" : "协作 Undo", out string conflict))
            {
                SendHistoryNotice(authorSteamId, false, $"协作历史冲突：{conflict}");
                AddEvent($"已拒绝 {entry.AuthorName} 的 {(redo ? "Redo" : "Undo")}：{conflict}");
                EmitStatus();
                return;
            }

            entry.Undone = !redo;
            revision++;
            inverse.Revision = revision;
            inverse.AuthorSteamId = entry.AuthorSteamId;
            inverse.AuthorName = entry.AuthorName;
            RecordOperation(inverse);
            Broadcast("operation.accepted", inverse);
            SendHistoryNotice(authorSteamId, true, redo ? "已重做你的上一步协作操作。" : "已撤销你的上一步协作操作。");
            AddEvent($"{entry.AuthorName} {(redo ? "重做" : "撤销")}了自己的操作 r{revision}。", entry.AuthorSteamId, entry.AuthorName);
            EmitStatus();
        }

        private CollabOperationBatch? FindHistoryEntry(string authorSteamId, bool redo)
        {
            for (int i = operationLog.Count - 1; i >= 0; i--)
            {
                CollabOperationBatch entry = operationLog[i];
                if (entry.Undone == redo && string.Equals(entry.AuthorSteamId, authorSteamId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private void SendHistoryNotice(string steamId, bool ok, string message)
        {
            if (string.Equals(steamId, SteamUser.GetSteamID().m_SteamID.ToString(), StringComparison.Ordinal))
            {
                if (ADOBase.editor != null)
                {
                    ADOBase.editor.ShowNotification(message);
                }

                AddEvent(message);
                EmitStatus();
                return;
            }

            if (ulong.TryParse(steamId, out ulong id))
            {
                transport.Send(new CSteamID(id), "history.notice", new CollabHistoryNotice
                {
                    Ok = ok,
                    Message = message
                }, revision);
            }
        }

        private void HandleHistoryNotice(JToken payload)
        {
            CollabHistoryNotice notice = payload.ToObject<CollabHistoryNotice>() ?? new CollabHistoryNotice();
            if (ADOBase.editor != null && !string.IsNullOrWhiteSpace(notice.Message))
            {
                ADOBase.editor.ShowNotification(notice.Message);
            }

            AddEvent(notice.Message);
            EmitStatus();
        }

        private void AddEvent(string message, string steamId = "", string actorName = "")
        {
            recentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            if (recentEvents.Count > 20)
            {
                recentEvents.RemoveAt(recentEvents.Count - 1);
            }

            CollabToastOverlay.Push(message, steamId, actorName);
        }

        private void EmitStatus()
        {
            Main.EmitOverlayEvent("collab.status", GetStatus());
        }

        private void Fail(string message)
        {
            lastError = message;
            syncState = "error";
            AddEvent(message);
            EmitStatus();
            Main.Mod?.Logger.Error(message);
        }

        private static void EnsureSteam()
        {
            if (!SteamIntegration.initialized)
            {
                throw new InvalidOperationException("Steamworks 尚未初始化，请确认通过 Steam 启动游戏。");
            }
        }

        private void EnsureLobby()
        {
            if (!InLobby)
            {
                throw new InvalidOperationException("当前不在协作房间中。");
            }
        }

        private static void EnsureEditorLevel()
        {
            if (!EditorStateAdapter.IsEditorReady)
            {
                throw new InvalidOperationException("请先在编辑器中打开或保存一个 .adofai 关卡。");
            }
        }

        private sealed class PendingOperationProposal
        {
            public string AuthorId { get; set; } = string.Empty;

            public string AuthorName { get; set; } = string.Empty;

            public CollabOperationBatch Batch { get; set; } = new CollabOperationBatch();
        }
    }
}
