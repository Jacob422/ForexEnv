using System;

namespace ForexEnv.Data
{
    public class Transaction
    {
        public int Id { get; set; }
        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = null!;
        
        public string CurrencyCode { get; set; } = null!;
        public string Type { get; set; } = null!; // "BUY" lub "SELL"
        public decimal Amount { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal TotalCostPLN { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }
}
