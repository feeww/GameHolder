using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Weapon/For Character", fileName = "NewCharacterWeapon")]
    public class CharacterWeaponDefinition : WeaponDefinition
    {
        [Tooltip("Stats this specific weapon can receive in level-up rewards. Inapplicable stats are excluded by weapon type.")]
        public WeaponUpgradeStats UpgradableStats = WeaponUpgradeStats.All;
        public WeaponUpgradeStats GetApplicableUpgradeStats(WeaponUpgradeStats available = WeaponUpgradeStats.All)
            => (WeaponUpgradeStats)(RewardRoll.StatMask(CharacterUpgradeStats.None, available & WeaponUpgradeStats.All, ToConfig(), 1) >> 2);
    }
}
