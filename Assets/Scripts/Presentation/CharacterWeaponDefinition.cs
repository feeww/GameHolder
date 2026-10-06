using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Weapon/For Character", fileName = "NewCharacterWeapon")]
    public class CharacterWeaponDefinition : WeaponDefinition
    {
        [Tooltip("Stats this specific weapon can receive in level-up rewards. Inapplicable stats are excluded by weapon type.")]
        public WeaponUpgradeStats UpgradableStats = WeaponUpgradeStats.All;
    }
}
