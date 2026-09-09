namespace GBCWorkHub.BIZ.Improvement
{
    /// <summary>
    /// 개선사항 요청/댓글의 소유권 판정. 반드시 세션 USER_ID(불변)로만 비교하고,
    /// 표시명/PC명으로 재해석하지 않는다.
    /// </summary>
    public static class ImprovementOwnership
    {
        public static bool IsOwner(long? currentUserId, long? resourceUserId)
        {
            return currentUserId.HasValue && resourceUserId.HasValue && currentUserId.Value == resourceUserId.Value;
        }

        public static bool CanManage(long? currentUserId, long? resourceUserId, bool isAdmin)
        {
            return isAdmin || IsOwner(currentUserId, resourceUserId);
        }
    }
}
