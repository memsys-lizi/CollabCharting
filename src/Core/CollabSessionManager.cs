using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal sealed class CollabSessionManager : IDisposable
    {
        private readonly RelayClient relay = new RelayClient();
        private readonly List<string> recentEvents = new List<string>();
        private readonly Dictionary<string, CollabLock> locks = new Dictionary<string, CollabLock>();
        private readonly List<CollabOperationBatch> operationLog = new List<CollabOperationBatch>();
        private readonly Dictionary<string, CollabOperationBatch> pendingLocalOperations = new Dictionary<string, CollabOperationBatch>();
        private readonly List<PendingOperationProposal> pendingOperationProposals = new List<PendingOperationProposal>();
        private readonly SortedDictionary<int, CollabOperationBatch> pendingAcceptedOperations = new SortedDictionary<int, CollabOperationBatch>();
        private readonly Dictionary<string, long> lastClientSeqByAuthor = new Dictionary<string, long>();
        private readonly HashSet<string> initialSyncSent = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<CollabOperationBatch> pendingPlaybackOperations = new List<CollabOperationBatch>();
        private readonly List<CollabMember> members = new List<CollabMember>();
        private string relayToken = string.Empty;
        private string localUserId = string.Empty;
        private string localName = string.Empty;
        private string roomId = string.Empty;
        private string hostUserId = string.Empty;
        private string lastError = string.Empty;
        private string syncState = "idle";
        private string pendingPlaybackReason = string.Empty;
        private string pendingPlaybackLevelPath = string.Empty;
        private int revision;
        private float syncProgress;
        private float lockHeartbeatTimer;
        private float pendingOperationResendTimer;
        private long nextClientSeq;
        private bool pendingPlaybackNoticeShown;
        private bool waitingForEditor;

        public bool InLobby => !string.IsNullOrWhiteSpace(roomId);

        public bool IsHost => InLobby && string.Equals(hostUserId, localUserId, StringComparison.Ordinal);

        public string LobbyId => roomId;

        public int Revision => revision;

        public IReadOnlyCollection<CollabLock> ActiveLocks => locks.Values;

        public bool IsWaitingForEditor => waitingForEditor;

        public string SyncState => syncState;

        public float SyncProgress => syncProgress;

        public bool IsBlockingUserInput =>
            InLobby &&
            !IsHost &&
            (waitingForEditor ||
             syncState == "joining" ||
             syncState == "syncing" ||
             syncState == "queued");

        public void Start()
        {
        }

        public CollabStatus GetStatus()
        {
            return new CollabStatus
            {
                AccountAvailable = IsAuthenticated,
                LocalUserId = localUserId,
                LocalName = localName,
                InLobby = InLobby,
                IsHost = IsHost,
                LobbyId = roomId,
                HostUserId = hostUserId,
                LevelName = EditorStateAdapter.CurrentLevelName,
                LevelPath = EditorStateAdapter.CurrentLevelPath,
                Revision = revision,
                SyncState = syncState,
                SyncProgress = syncProgress,
                LastError = lastError,
                Members = new List<CollabMember>(members),
                Locks = new List<CollabLock>(locks.Values),
                RecentEvents = new List<string>(recentEvents)
            };
        }

        public CollabAuthStart StartAuth()
        {
            return RelayHttpClient.StartAuth();
        }

        public object PollAuth(string loginId)
        {
            CollabAuthPoll result = RelayHttpClient.PollAuth(loginId);
            if (result.Status == "ok" && result.User != null && !string.IsNullOrWhiteSpace(result.RelayToken))
            {
                LoginWithToken(result.RelayToken, result.User);
            }

            return result;
        }

        public object LoginWithToken(string token, CollabAuthUser user)
        {
            if (string.IsNullOrWhiteSpace(token) || user == null || string.IsNullOrWhiteSpace(user.UserId))
            {
                throw new InvalidOperationException("登录凭据无效。");
            }

            relayToken = token;
            localUserId = user.UserId;
            localName = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname;
            relay.Connect(relayToken);
            AddEvent($"已登录 ADOFAITools：{localName}", localUserId, localName);
            EmitStatus();
            return GetStatus();
        }

        public object CreateLobby()
        {
            EnsureAuthenticated();
            EnsureEditorLevel();
            if (InLobby)
            {
                LeaveLobby();
            }

            syncState = "creating";
            syncProgress = 0f;
            relay.CreateRoom();
            AddEvent("正在创建协作房间。");
            EmitStatus();
            return GetStatus();
        }

        public object JoinLobby(string id)
        {
            EnsureAuthenticated();
            if (!ADOBase.isLevelEditor && !waitingForEditor)
            {
                throw new InvalidOperationException("请先进入关卡编辑器，再通过“协作”面板加入房间。");
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("房间码不能为空。");
            }

            syncState = "joining";
            syncProgress = 0f;
            relay.JoinRoom(id);
            EmitStatus();
            return GetStatus();
        }

        public object LeaveLobby()
        {
            if (InLobby)
            {
                relay.LeaveRoom();
            }

            ResetRoomState();
            AddEvent("已离开协作房间。");
            EmitStatus();
            return GetStatus();
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
                ADOBase.editor?.ShowNotification($"{remoteLock.OwnerName} 正在编辑，当前对象只读");
                AddEvent($"{target} 已被 {remoteLock.OwnerName} 锁定。");
                EmitStatus();
                return remoteLock;
            }

            var collabLock = new CollabLock
            {
                Target = target,
                OwnerUserId = localUserId,
                OwnerName = localName,
                ExpiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeSeconds()
            };
            ReleaseOtherLocalLocks(localUserId, target);
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

            if (string.Equals(existing.OwnerUserId, localUserId, StringComparison.Ordinal))
            {
                return false;
            }

            collabLock = existing;
            return true;
        }

        public bool IsLocalLock(CollabLock collabLock)
        {
            return collabLock != null && string.Equals(collabLock.OwnerUserId, localUserId, StringComparison.Ordinal);
        }

        public void Update(float dt)
        {
            while (relay.TryDequeue(out RelayServerEvent serverEvent))
            {
                HandleServerEvent(serverEvent);
            }

            PruneLocks();
            RefreshLocalLocks(dt);
            RetryPendingLocalOperations(dt);
            DrainPendingOperationProposals();
            ApplyPendingPlaybackSyncIfReady();
        }

        public void PublishLocalOperationBatch(CollabOperationBatch batch)
        {
            if (!InLobby || batch == null || batch.Ops.Count == 0)
            {
                return;
            }

            batch.AuthorUserId = localUserId;
            batch.AuthorName = localName;
            batch.BaseRevision = revision;
            if (string.IsNullOrWhiteSpace(batch.OperationId))
            {
                batch.OperationId = Guid.NewGuid().ToString("N");
            }

            batch.ClientSeq = ++nextClientSeq;
            if (!AttachRequiredFiles(batch, out string resourceError))
            {
                ADOBase.editor?.ShowNotification(resourceError);
                AddEvent(resourceError, localUserId, localName);
                EmitStatus();
                return;
            }

            UploadRequiredResources(batch);
            if (IsHost)
            {
                revision++;
                batch.Revision = revision;
                RecordOperation(batch);
                Broadcast("operation.accepted", batch);
                AddEvent($"{localName} 修改了谱面 r{revision}（{DescribeBatch(batch)}）。", localUserId, localName);
                EmitStatus();
            }
            else
            {
                pendingLocalOperations[batch.OperationId] = OperationDiffUtility.CloneBatch(batch);
                relay.SendToHost("operation.proposal", batch, revision);
                AddEvent($"已向房主提交本地操作（{DescribeBatch(batch)}）。", localUserId, localName);
                EmitStatus();
            }
        }

        public void RequestCollaborativeUndoRedo(bool redo)
        {
            EnsureLobby();
            if (IsHost)
            {
                HandleHistoryRequest(localUserId, redo);
                return;
            }

            relay.SendToHost("operation.undoRequest", new CollabHistoryRequest { Redo = redo }, revision);
            AddEvent(redo ? "已向房主请求协作 Redo。" : "已向房主请求协作 Undo。");
            EmitStatus();
        }

        public void MarkEditorAvailable()
        {
            waitingForEditor = false;
        }

        public void Dispose()
        {
            relay.Dispose();
        }

        private bool IsAuthenticated => !string.IsNullOrWhiteSpace(relayToken) && !string.IsNullOrWhiteSpace(localUserId);

        private void HandleServerEvent(RelayServerEvent serverEvent)
        {
            try
            {
                switch (serverEvent.Type)
                {
                    case "room.state":
                        HandleRoomState(serverEvent.Payload?.ToObject<RelayRoomState>() ?? new RelayRoomState());
                        break;
                    case "room.closed":
                        string reason = serverEvent.Payload?.Value<string>("reason") ?? "协作房间已关闭。";
                        ResetRoomState();
                        AddEvent(reason);
                        ADOBase.editor?.ShowNotification(reason);
                        EmitStatus();
                        break;
                    case "relay":
                        RelayPayload relayPayload = serverEvent.Payload?.ToObject<RelayPayload>() ?? new RelayPayload();
                        HandleRelayEnvelope(serverEvent.SenderUserId, relayPayload.Type, relayPayload.Payload);
                        break;
                    case "error":
                        Fail(serverEvent.Payload?.Value<string>("message") ?? "Relay error");
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        private void HandleRoomState(RelayRoomState state)
        {
            bool wasInLobby = InLobby;
            string previousHost = hostUserId;
            roomId = state.RoomId;
            hostUserId = state.HostUserId;
            members.Clear();
            foreach (RelayRoomMember member in state.Members)
            {
                members.Add(new CollabMember
                {
                    UserId = member.UserId,
                    Name = member.Name,
                    IsHost = member.IsHost,
                    IsLocal = string.Equals(member.UserId, localUserId, StringComparison.Ordinal)
                });
            }

            if (IsHost && revision == 0)
            {
                revision = 1;
                syncState = "hosting";
                syncProgress = 1f;
                OperationCapture.ResetBaseline();
                EntityIdRegistry.InitializeFromCurrentLevel();
                AddEvent("已创建协作房间。", localUserId, localName);
            }
            else if (!IsHost && (!wasInLobby || previousHost != hostUserId))
            {
                syncState = "syncing";
                syncProgress = 0f;
                relay.SendToHost("sync.request", new { }, revision);
                AddEvent("已加入协作房间，正在请求初始同步。", localUserId, localName);
            }

            EmitStatus();
        }

        private void HandleRelayEnvelope(string senderUserId, string type, JToken? payload)
        {
            if (payload == null)
            {
                return;
            }

            switch (type)
            {
                case "sync.request":
                    if (IsHost)
                    {
                        SendInitialSync(senderUserId);
                    }
                    break;
                case "level.initial":
                    HandleInitialLevel(payload);
                    break;
                case "operation.accepted":
                    HandleOperationAccepted(payload);
                    break;
                case "operation.proposal":
                    HandleOperationProposal(senderUserId, payload);
                    break;
                case "operation.rejected":
                    HandleOperationRejected(payload);
                    break;
                case "lock.update":
                    HandleLockUpdate(payload);
                    break;
                case "lock.release":
                    HandleLockRelease(payload);
                    break;
                case "operation.undoRequest":
                    HandleHistoryRequest(senderUserId, payload.ToObject<CollabHistoryRequest>()?.Redo == true);
                    break;
                case "history.notice":
                    HandleHistoryNotice(payload);
                    break;
            }
        }

        private void SendInitialSync(string targetUserId)
        {
            if (!IsHost || !EditorStateAdapter.IsEditorReady || string.IsNullOrWhiteSpace(targetUserId) || initialSyncSent.Contains(targetUserId))
            {
                return;
            }

            initialSyncSent.Add(targetUserId);
            syncState = "sending";
            ResourceManifest manifest = ResourceSync.BuildManifest(EditorStateAdapter.CurrentLevelPath);
            UploadManifestResources(manifest);
            relay.SendToUser(targetUserId, "level.initial", new CollabInitialLevel
            {
                Revision = revision,
                LevelText = EditorStateAdapter.EncodeCurrentLevel(),
                LevelRelativePath = Path.GetFileName(EditorStateAdapter.CurrentLevelPath),
                Manifest = manifest
            }, revision);
            syncState = "hosting";
            syncProgress = 1f;
            AddEvent("已向新成员发送完整关卡资源。", targetUserId, GetMemberName(targetUserId));
            EmitStatus();
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
            DownloadManifestResources(initial.Manifest);
            string levelPath = ResourceSync.ResolveCachePath(roomId, initial.LevelRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(levelPath) ?? string.Empty);
            File.WriteAllText(levelPath, initial.LevelText);
            syncState = "syncing";
            syncProgress = 0.95f;
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

            if (!EnsureRequiredResources(batch, out List<string> missingResources))
            {
                pendingAcceptedOperations[batch.Revision] = batch;
                AddEvent($"正在等待协作资源：{FormatMissingResources(missingResources)}。", batch.AuthorUserId, batch.AuthorName);
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

            AddEvent($"{batch.AuthorName} 的操作已同步 r{revision}（{DescribeBatch(batch)}）。", batch.AuthorUserId, batch.AuthorName);
            EmitStatus();
        }

        private void DrainAcceptedOperations()
        {
            while (pendingAcceptedOperations.TryGetValue(revision + 1, out CollabOperationBatch batch))
            {
                if (!EnsureRequiredResources(batch, out _))
                {
                    return;
                }

                pendingAcceptedOperations.Remove(revision + 1);
                ApplyAcceptedOperation(batch);
            }
        }

        private void HandleOperationProposal(string senderUserId, JToken payload)
        {
            if (!IsHost)
            {
                return;
            }

            CollabOperationBatch batch = payload.ToObject<CollabOperationBatch>() ?? new CollabOperationBatch();
            string authorName = string.IsNullOrWhiteSpace(batch.AuthorName) ? GetMemberName(senderUserId) : batch.AuthorName;
            batch.AuthorUserId = senderUserId;
            batch.AuthorName = authorName;

            if (TryFindAcceptedOperation(batch.OperationId, out CollabOperationBatch? accepted) && accepted != null)
            {
                relay.SendToUser(senderUserId, "operation.accepted", accepted, revision);
                return;
            }

            var proposal = new PendingOperationProposal
            {
                AuthorId = senderUserId,
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

            if (!EnsureRequiredResources(batch, out List<string> missingResources))
            {
                waitReason = $"等待资源：{FormatMissingResources(missingResources)}";
                return false;
            }

            if (!ValidateClientSequence(batch, authorId, out waitReason, out bool rejectedSequence))
            {
                return rejectedSequence;
            }

            if (EditorPlaybackState.IsPreviewPlaying)
            {
                waitReason = "房主预览播放中";
                return false;
            }

            CollabOperationBatch transformed = OperationDiffUtility.CloneBatch(batch);
            if (!OperationDiffUtility.TransformForAcceptedOperations(transformed, operationLog, out string transformConflict))
            {
                RejectOperation(authorId, transformed.OperationId, transformConflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            if (!ValidateLocks(transformed, authorId, out string lockConflict))
            {
                RejectOperation(authorId, transformed.OperationId, lockConflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            if (!OperationApplier.TryApply(transformed, $"client-change r{revision + 1}", out string conflict))
            {
                RejectOperation(authorId, transformed.OperationId, conflict);
                MarkClientSequenceConsumed(authorId, batch.ClientSeq);
                return true;
            }

            revision++;
            transformed.Revision = revision;
            transformed.ClientSeq = batch.ClientSeq;
            transformed.AuthorUserId = authorId;
            transformed.AuthorName = authorName;
            MarkClientSequenceConsumed(authorId, batch.ClientSeq);
            UploadRequiredResources(transformed);
            RecordOperation(transformed);
            Broadcast("operation.accepted", transformed);
            AddEvent($"{authorName} 的操作已同步 r{revision}（{DescribeBatch(transformed)}）。", authorId, authorName);
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
            foreach (CollabOperationBatch batch in new List<CollabOperationBatch>(pendingLocalOperations.Values))
            {
                relay.SendToHost("operation.proposal", batch, revision);
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

            ADOBase.editor?.ShowNotification(reason);
            AddEvent(reason);
            EmitStatus();
        }

        private void RejectOperation(string authorId, string operationId, string reason)
        {
            AddEvent($"已拒绝成员操作：{reason}", authorId, GetMemberName(authorId));
            relay.SendToUser(authorId, "operation.rejected", new { operationId, reason }, revision);
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

                    if (!string.Equals(collabLock.OwnerUserId, authorId, StringComparison.Ordinal))
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

        private bool EnsureRequiredResources(CollabOperationBatch batch, out List<string> missingResources)
        {
            missingResources = new List<string>();
            if (batch == null || batch.RequiredFiles.Count == 0)
            {
                return true;
            }

            if (HasRequiredResources(batch, out missingResources))
            {
                return true;
            }

            foreach (ResourceManifestEntry file in batch.RequiredFiles)
            {
                if (!missingResources.Contains(file.RelativePath))
                {
                    continue;
                }

                try
                {
                    byte[] bytes = RelayHttpClient.DownloadResource(roomId, relayToken, file);
                    if (IsHost && EditorStateAdapter.IsEditorReady)
                    {
                        ResourceSync.WriteFileBytesToLevelRoot(EditorStateAdapter.CurrentLevelPath, file.RelativePath, bytes);
                    }
                    else
                    {
                        ResourceSync.WriteFileBytes(roomId, file.RelativePath, bytes);
                    }
                }
                catch (Exception ex)
                {
                    Main.Mod?.Logger.Warning($"Failed to download collab resource {file.RelativePath}: {ex.Message}");
                }
            }

            return HasRequiredResources(batch, out missingResources);
        }

        private bool HasRequiredResources(CollabOperationBatch batch, out List<string> missingResources)
        {
            if (IsHost && EditorStateAdapter.IsEditorReady)
            {
                return ResourceSync.LevelRootHasFiles(EditorStateAdapter.CurrentLevelPath, batch.RequiredFiles, out missingResources);
            }

            return ResourceSync.CacheHasFiles(roomId, batch.RequiredFiles, out missingResources);
        }

        private bool ValidateClientSequence(CollabOperationBatch batch, string authorId, out string waitReason, out bool rejected)
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

            return $"{missingFiles[0]}, {missingFiles[1]}, {missingFiles[2]} 等 {missingFiles.Count} 个文件";
        }

        private void UploadManifestResources(ResourceManifest manifest)
        {
            if (!EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            int total = Math.Max(1, manifest.Files.Count);
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ResourceManifestEntry file = manifest.Files[i];
                string fullPath = ResourceSync.ResolveLevelPath(EditorStateAdapter.CurrentLevelPath, file.RelativePath);
                RelayHttpClient.UploadResource(roomId, relayToken, file, fullPath);
                syncProgress = (i + 1) / (float)total;
            }
        }

        private void DownloadManifestResources(ResourceManifest manifest)
        {
            if (manifest == null || manifest.Files.Count == 0)
            {
                return;
            }

            int total = Math.Max(1, manifest.Files.Count);
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ResourceManifestEntry file = manifest.Files[i];
                if (ResourceSync.CacheHasFiles(roomId, new[] { file }, out _))
                {
                    continue;
                }

                byte[] bytes = RelayHttpClient.DownloadResource(roomId, relayToken, file);
                ResourceSync.WriteFileBytes(roomId, file.RelativePath, bytes);
                syncProgress = (i + 1) / (float)total;
            }
        }

        private void UploadRequiredResources(CollabOperationBatch batch)
        {
            if (batch == null || batch.RequiredFiles.Count == 0 || !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            foreach (ResourceManifestEntry file in batch.RequiredFiles)
            {
                try
                {
                    string fullPath = ResourceSync.ResolveLevelPath(EditorStateAdapter.CurrentLevelPath, file.RelativePath);
                    RelayHttpClient.UploadResource(roomId, relayToken, file, fullPath);
                }
                catch (Exception ex)
                {
                    Main.Mod?.Logger.Warning($"Failed to upload collab resource {file.RelativePath}: {ex.Message}");
                }
            }
        }

        private void Broadcast(string type, object payload)
        {
            if (InLobby)
            {
                relay.Broadcast(type, payload, revision);
            }
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

        private void RemoveLocksForOwner(string ownerUserId)
        {
            var release = new List<string>();
            foreach (KeyValuePair<string, CollabLock> pair in locks)
            {
                if (string.Equals(pair.Value.OwnerUserId, ownerUserId, StringComparison.Ordinal))
                {
                    release.Add(pair.Key);
                }
            }

            foreach (string target in release)
            {
                locks.Remove(target);
            }
        }

        private void ReleaseOtherLocalLocks(string ownerUserId, string exceptTarget)
        {
            var release = new List<string>();
            foreach (KeyValuePair<string, CollabLock> pair in locks)
            {
                if (pair.Key != exceptTarget &&
                    string.Equals(pair.Value.OwnerUserId, ownerUserId, StringComparison.Ordinal))
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
            bool changed = false;
            foreach (CollabLock collabLock in new List<CollabLock>(locks.Values))
            {
                if (!string.Equals(collabLock.OwnerUserId, localUserId, StringComparison.Ordinal))
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

        private void HandleHistoryRequest(string authorUserId, bool redo)
        {
            if (!IsHost)
            {
                return;
            }

            CollabOperationBatch? entry = FindHistoryEntry(authorUserId, redo);
            if (entry == null)
            {
                SendHistoryNotice(authorUserId, false, redo ? "没有可重做的个人操作。" : "没有可撤销的个人操作。");
                return;
            }

            CollabOperationBatch inverse = OperationDiffUtility.CreateInverse(entry, redo ? "collab-redo" : "collab-undo");
            inverse.BaseRevision = revision;
            if (!OperationApplier.TryApply(inverse, redo ? "协作 Redo" : "协作 Undo", out string conflict))
            {
                SendHistoryNotice(authorUserId, false, $"协作历史冲突：{conflict}");
                AddEvent($"已拒绝 {entry.AuthorName} 的 {(redo ? "Redo" : "Undo")}：{conflict}");
                EmitStatus();
                return;
            }

            entry.Undone = !redo;
            revision++;
            inverse.Revision = revision;
            inverse.AuthorUserId = entry.AuthorUserId;
            inverse.AuthorName = entry.AuthorName;
            RecordOperation(inverse);
            Broadcast("operation.accepted", inverse);
            SendHistoryNotice(authorUserId, true, redo ? "已重做你的上一步协作操作。" : "已撤销你的上一步协作操作。");
            AddEvent($"{entry.AuthorName} {(redo ? "重做" : "撤销")}了自己的操作 r{revision}。", entry.AuthorUserId, entry.AuthorName);
            EmitStatus();
        }

        private CollabOperationBatch? FindHistoryEntry(string authorUserId, bool redo)
        {
            for (int i = operationLog.Count - 1; i >= 0; i--)
            {
                CollabOperationBatch entry = operationLog[i];
                if (entry.Undone == redo && string.Equals(entry.AuthorUserId, authorUserId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private void SendHistoryNotice(string userId, bool ok, string message)
        {
            if (string.Equals(userId, localUserId, StringComparison.Ordinal))
            {
                ADOBase.editor?.ShowNotification(message);
                AddEvent(message);
                EmitStatus();
                return;
            }

            relay.SendToUser(userId, "history.notice", new CollabHistoryNotice { Ok = ok, Message = message }, revision);
        }

        private void HandleHistoryNotice(JToken payload)
        {
            CollabHistoryNotice notice = payload.ToObject<CollabHistoryNotice>() ?? new CollabHistoryNotice();
            if (!string.IsNullOrWhiteSpace(notice.Message))
            {
                ADOBase.editor?.ShowNotification(notice.Message);
            }

            AddEvent(notice.Message);
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

            MainThreadDispatcher.Enqueue(() =>
            {
                EditorStateAdapter.LoadLevelFromCache(levelPath);
                syncState = "synced";
                syncProgress = 1f;
                EmitStatus();
            });
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
                MainThreadDispatcher.Enqueue(() =>
                {
                    EditorStateAdapter.LoadLevelFromCache(levelPath);
                    syncState = "synced";
                    syncProgress = 1f;
                    AddEvent("预览结束，已应用排队的初始同步。");
                    EmitStatus();
                });
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

        private void ResetRoomState()
        {
            roomId = string.Empty;
            hostUserId = string.Empty;
            revision = 0;
            locks.Clear();
            members.Clear();
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
        }

        private string GetMemberName(string userId)
        {
            foreach (CollabMember member in members)
            {
                if (string.Equals(member.UserId, userId, StringComparison.Ordinal))
                {
                    return member.Name;
                }
            }

            return string.IsNullOrWhiteSpace(userId) ? "成员" : userId;
        }

        private void AddEvent(string message, string userId = "", string actorName = "")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            recentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            if (recentEvents.Count > 20)
            {
                recentEvents.RemoveAt(recentEvents.Count - 1);
            }

            CollabToastOverlay.Push(message, userId, actorName);
        }

        private void EmitStatus()
        {
        }

        private void Fail(string message)
        {
            lastError = message;
            syncState = "error";
            AddEvent(message);
            EmitStatus();
            Main.Mod?.Logger.Error(message);
        }

        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException("请先登录 ADOFAITools 账号。");
            }

            relay.Connect(relayToken);
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
