using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GBCWorkHub.DTO
{
    /// <summary>
    /// GBCWORKHUB_TFS::{JSON} 페이로드
    /// </summary>
    public class TfsRecentChangesetsPayload
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("schemaVersion")]
        public int? SchemaVersion { get; set; }

        [JsonProperty("collectionUrl")]
        public string CollectionUrl { get; set; }

        [JsonProperty("serverPath")]
        public string ServerPath { get; set; }

        [JsonProperty("queryMode")]
        public string QueryMode { get; set; }

        [JsonProperty("sessionStartAt")]
        public string SessionStartAt { get; set; }

        [JsonProperty("sessionEndAt")]
        public string SessionEndAt { get; set; }

        [JsonProperty("sessionToken")]
        public string SessionToken { get; set; }

        [JsonProperty("remoteComputerName")]
        public string RemoteComputerName { get; set; }

        [JsonProperty("sourceClientName")]
        public string SourceClientName { get; set; }

        [JsonProperty("collectedAt")]
        public string CollectedAt { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("computerName")]
        public string ComputerName { get; set; }

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("deliveryId")]
        public string DeliveryId { get; set; }

        [JsonProperty("deliveryMode")]
        public string DeliveryMode { get; set; }

        [JsonProperty("deliverySentAt")]
        public string DeliverySentAt { get; set; }

        [JsonProperty("authorizedUserId")]
        public string AuthorizedUserId { get; set; }

        [JsonProperty("returnedItemCount")]
        public int? ReturnedItemCount { get; set; }

        [JsonProperty("changesets")]
        public List<TfsChangesetItem> Changesets { get; set; }

        /// <summary>computerName 또는 remoteComputerName</summary>
        public string ResolveComputerName()
        {
            if (!string.IsNullOrWhiteSpace(ComputerName))
                return ComputerName.Trim();
            if (!string.IsNullOrWhiteSpace(RemoteComputerName))
                return RemoteComputerName.Trim();
            return null;
        }
    }

    public class TfsChangesetItem
    {
        [JsonProperty("changesetId")]
        public int ChangesetId { get; set; }

        [JsonProperty("authorName")]
        public string AuthorName { get; set; }

        [JsonProperty("authorId")]
        public string AuthorId { get; set; }

        [JsonProperty("checkedInAt")]
        public string CheckedInAt { get; set; }

        [JsonProperty("comment")]
        public string Comment { get; set; }

        [JsonProperty("changedFileCount")]
        public int? ChangedFileCount { get; set; }

        /// <summary>원격 Collect 스크립트 기본 키: changedFiles.</summary>
        [JsonProperty("changedFiles")]
        public List<TfsChangedFileItem> ChangedFiles { get; set; }

        /// <summary>레거시 키 files → ChangedFiles로 흡수.</summary>
        [JsonProperty("files")]
        private List<TfsChangedFileItem> FilesLegacy
        {
            set
            {
                if (value == null)
                    return;
                if (ChangedFiles == null || ChangedFiles.Count == 0)
                    ChangedFiles = value;
            }
        }

        /// <summary>기존 Parser/UI 호환용. ChangedFiles와 동일 참조.</summary>
        [JsonIgnore]
        public List<TfsChangedFileItem> Files
        {
            get { return ChangedFiles; }
            set { ChangedFiles = value; }
        }
    }

    public class TfsChangedFileItem
    {
        [JsonProperty("changeType")]
        public string ChangeType { get; set; }

        [JsonProperty("itemType")]
        public string ItemType { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        /// <summary>원격 스크립트 기본 키: path (TFVC server path).</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>레거시 키 serverPath → Path로 흡수.</summary>
        [JsonProperty("serverPath")]
        private string ServerPathLegacy
        {
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Path))
                    Path = value;
            }
        }

        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>기존 Parser/UI 호환용. Path와 동일.</summary>
        [JsonIgnore]
        public string ServerPath
        {
            get { return Path; }
            set { Path = value; }
        }
    }

    /// <summary>
    /// XSUP.MSDWHTFS 저장용
    /// </summary>
    public class TfsWorkLogRecord
    {
        public string CollectionUrl { get; set; }
        public string ServerPath { get; set; }
        public int ChangesetId { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public string OriginalComment { get; set; }
        public string WorkTitle { get; set; }
        public string WorkContent { get; set; }
        public string Note { get; set; }
        public int ChangedFileCount { get; set; }
        public string ChangedFileSummary { get; set; }
        public string RemoteComputerName { get; set; }
        public string SourceClientName { get; set; }
        public string QueryMode { get; set; }
        public DateTime? SessionStartAt { get; set; }
        public DateTime? SessionEndAt { get; set; }
        public string SessionToken { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
