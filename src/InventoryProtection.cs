namespace MikkoMods
{
    public static class InventoryProtection
    {
        public static bool Keep(string type, bool equipped, bool hotbar, bool food, bool quest, bool explicitlyProtected)
        {
            if (equipped || hotbar || food || quest || explicitlyProtected) return true;
            // Resource/loot categories may leave the backpack. All equipment,
            // consumables, utility/misc items and unknown future types stay with you.
            switch (type)
            {
                case "Material":
                case "Trophy":
                case "Fish":
                case "Customization":
                    return false;
                default:
                    return true;
            }
        }
    }
}
