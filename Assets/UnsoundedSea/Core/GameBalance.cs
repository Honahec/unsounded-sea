namespace UnsoundedSea.Core
{
    public static class GameBalance
    {
        public const int MaxHealth = 60;
        public const int MaxSanity = 30;
        public const int MaxSupplies = 8;
        public const int InitialSupplies = 3;

        // Out-of-battle recovery. Sanity is the scarce voyage resource, so every source stays small and paid calm costs two supplies.
        public const int RestHealth = 10;
        public const int RestSanity = 2;
        public const int RepairHealth = 16;
        public const int CalmSupplyCost = 2;
        public const int CalmSanity = 6;
        public const int SealSanity = 2;
        public const int EventSanity = 2;
        public const int TeaSanity = 3;
    }
}
