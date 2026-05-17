// ============================================================================
// EconomySystem.cs — Money and economy management
// ============================================================================
// PURPOSE:
//   Manages the player's money, transactions, and the in-game economy.
//   Money is earned through missions, side jobs, and found in the world.
//   Money is spent on weapons, vehicles, safehouses, and services.
//
// ECONOMY BALANCE:
//   - Story missions: $500 - $5,000
//   - Side missions: $100 - $1,000
//   - Taxi driving: $20 - $100 per fare
//   - Weapon prices: $200 - $10,000
//   - Vehicle prices: $5,000 - $100,000
//   - Safehouse prices: $10,000 - $500,000
//   - Medical treatment: $100 - $500
//   - Wanted level bribe: $500 per star
// ============================================================================

using UnityEngine;
using ShadowCity.Core;

namespace ShadowCity.Systems.Economy
{
    public class EconomySystem : MonoBehaviour
    {
        private static EconomySystem _instance;
        public static EconomySystem Instance => _instance;

        [Header("Starting Money")]
        [SerializeField] private int _startingMoney = 500;

        // State
        private int _currentMoney;
        private int _totalEarned;
        private int _totalSpent;

        // Events
        public System.Action<int, int> OnMoneyChanged; // oldAmount, newAmount

        public int CurrentMoney => _currentMoney;
        public int TotalEarned => _totalEarned;
        public int TotalSpent => _totalSpent;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _currentMoney = _startingMoney;
        }

        /// <summary>
        /// Add money to the player's wallet.
        /// </summary>
        public void AddMoney(int amount, string source = "")
        {
            if (amount <= 0) return;

            int oldAmount = _currentMoney;
            _currentMoney += amount;
            _totalEarned += amount;

            EventBus.Publish(new MoneyChangedEvent
            {
                OldAmount = oldAmount,
                NewAmount = _currentMoney
            });

            OnMoneyChanged?.Invoke(oldAmount, _currentMoney);

            Debug.Log($"[Economy] +${amount} from {source}. Balance: ${_currentMoney}");
        }

        /// <summary>
        /// Attempt to spend money. Returns true if the player has enough.
        /// </summary>
        public bool TrySpend(int amount, string purpose = "")
        {
            if (amount <= 0) return true;
            if (_currentMoney < amount) return false;

            int oldAmount = _currentMoney;
            _currentMoney -= amount;
            _totalSpent += amount;

            EventBus.Publish(new MoneyChangedEvent
            {
                OldAmount = oldAmount,
                NewAmount = _currentMoney
            });

            OnMoneyChanged?.Invoke(oldAmount, _currentMoney);

            Debug.Log($"[Economy] -${amount} for {purpose}. Balance: ${_currentMoney}");
            return true;
        }

        /// <summary>
        /// Check if the player can afford something without spending.
        /// </summary>
        public bool CanAfford(int amount)
        {
            return _currentMoney >= amount;
        }

        /// <summary>
        /// Set money directly (for save/load).
        /// </summary>
        public void SetMoney(int amount)
        {
            int oldAmount = _currentMoney;
            _currentMoney = Mathf.Max(0, amount);
            OnMoneyChanged?.Invoke(oldAmount, _currentMoney);
        }
    }
}
