using ADOFAI;

namespace CollabCharting
{
    internal static class EditorLockTargets
    {
        public const string EventIdPrefix = "eventId:";
        public const string DecorationIdPrefix = "decorationId:";

        public static string Floor(int floorId)
        {
            return $"floor:{floorId}";
        }

        public static string Event(int floor, LevelEventType eventType, int eventIndex)
        {
            string type = eventType.ToString();
            return EntityIdRegistry.TryGetEventEntityId(floor, type, eventIndex, out string entityId)
                ? EventIdPrefix + entityId
                : LegacyEvent(floor, eventType, eventIndex);
        }

        public static string Decoration(LevelEvent levelEvent)
        {
            int index = scrDecorationManager.GetDecorationIndex(levelEvent);
            if (EntityIdRegistry.TryGetEntityId("decoration", index, out string entityId))
            {
                return DecorationIdPrefix + entityId;
            }

            return $"decoration:{index}";
        }

        public static string LegacyEvent(int floor, LevelEventType eventType, int eventIndex)
        {
            return $"event:{floor}:{eventType}:{eventIndex}";
        }
    }
}
