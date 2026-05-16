// ============================================================================
// InventorySystem.cs — Item inventory management
// ============================================================================
// PURPOSE:
//   Manages the player's inventory of consumable items, key items, and
//   collectibles. Weapons have their own system (WeaponSystem).
//
// ITEM TYPES:
//   - Consumables: Health kits, body armor, food/drinks
//   - Key Items: Mission-critical objects (keycards, phones, evidence)
//   - Collectibles: Hidden packages, stunt jumps, unique items
//   - Ammo: Ammunition for different weapon types
//
// DATA-DRIVEN:
//   Items are defined as ScriptableObjects. To add a new item:
//   1. Create a new ItemData asset (Create > ShadowCity > Item Data)
//   2. Fill in the properties
//   3. The item is now available in the game
//   No code changes needed.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Inventory
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "ShadowCity/Item Data")]
    public class ItemData : ScriptableObject
    {
        public string ItemId;
        public string DisplayName;
        public string Description;
        public Sprite Icon;
        public ItemCategory Category;
        public int MaxStack = 1;
        public bool IsKeyItem;        // Key items can't be dropped or sold
        public int BuyPrice;
        public int SellPrice;

        [Header("Consumable Effects")]
        public float HealthRestore;
        public float ArmorRestore;
        public float StaminaRestore;
    }

    public enum ItemCategory
    {
        Consumable,
        KeyItem,
        Collectible,
        Ammo,
        Clothing
    }

    [System.Serializable]
    public class InventorySlot
    {
        public ItemData Item;
        public int Quantity;

        public InventorySlot(ItemData item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        public bool IsFull => Quantity >= Item.MaxStack;
    }

    public class InventorySystem : MonoBehaviour
    {
        private static InventorySystem _instance;
        public static InventorySystem Instance => _instance;

        [Header("Capacity")]
        [SerializeField] private int _maxSlots = 20;

        private List<InventorySlot> _slots = new List<InventorySlot>();

        public System.Action OnInventoryChanged;
        public int SlotCount => _slots.Count;
        public int MaxSlots => _maxSlots;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Add an item to the inventory. Returns true if successfully added.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            // Try to stack with existing
            InventorySlot existingSlot = _slots.Find(s => s.Item.ItemId == item.ItemId && !s.IsFull);

            if (existingSlot != null)
            {
                int canAdd = item.MaxStack - existingSlot.Quantity;
                int toAdd = Mathf.Min(quantity, canAdd);
                existingSlot.Quantity += toAdd;
                quantity -= toAdd;
            }

            // Add remaining to new slots
            while (quantity > 0 && _slots.Count < _maxSlots)
            {
                int toAdd = Mathf.Min(quantity, item.MaxStack);
                _slots.Add(new InventorySlot(item, toAdd));
                quantity -= toAdd;
            }

            if (quantity > 0)
            {
                Debug.Log("[Inventory] Inventory full! Could not add all items.");
                return false;
            }

            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Remove an item from inventory. Returns true if successfully removed.
        /// </summary>
        public bool RemoveItem(string itemId, int quantity = 1)
        {
            int remaining = quantity;

            for (int i = _slots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                if (_slots[i].Item.ItemId != itemId) continue;

                if (_slots[i].Quantity <= remaining)
                {
                    remaining -= _slots[i].Quantity;
                    _slots.RemoveAt(i);
                }
                else
                {
                    _slots[i].Quantity -= remaining;
                    remaining = 0;
                }
            }

            if (remaining == 0)
            {
                OnInventoryChanged?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Use a consumable item (apply effects and remove from inventory).
        /// </summary>
        public bool UseItem(string itemId)
        {
            InventorySlot slot = _slots.Find(s => s.Item.ItemId == itemId);
            if (slot == null) return false;

            ItemData item = slot.Item;

            if (item.Category != ItemCategory.Consumable) return false;

            // Apply effects
            Health.HealthSystem health = GetComponent<Health.HealthSystem>();
            if (health != null)
            {
                if (item.HealthRestore > 0) health.Heal(item.HealthRestore);
                if (item.ArmorRestore > 0) health.AddArmor(item.ArmorRestore);
            }

            RemoveItem(itemId, 1);
            return true;
        }

        /// <summary>
        /// Check if the player has a specific item.
        /// </summary>
        public bool HasItem(string itemId, int quantity = 1)
        {
            int totalCount = 0;
            foreach (InventorySlot slot in _slots)
            {
                if (slot.Item.ItemId == itemId)
                {
                    totalCount += slot.Quantity;
                }
            }
            return totalCount >= quantity;
        }

        /// <summary>
        /// Get the total count of a specific item.
        /// </summary>
        public int GetItemCount(string itemId)
        {
            int count = 0;
            foreach (InventorySlot slot in _slots)
            {
                if (slot.Item.ItemId == itemId)
                {
                    count += slot.Quantity;
                }
            }
            return count;
        }

        /// <summary>
        /// Get all inventory slots (for UI display).
        /// </summary>
        public List<InventorySlot> GetAllSlots()
        {
            return new List<InventorySlot>(_slots);
        }

        /// <summary>
        /// Clear all items (for new game).
        /// </summary>
        public void ClearInventory()
        {
            _slots.Clear();
            OnInventoryChanged?.Invoke();
        }
    }
}
