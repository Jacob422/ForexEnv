using System;
using System.Collections.Generic;

namespace ForexEnv.Data
{
    public class Wallet
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string Name { get; set; } = "Główny";
        public decimal BalancePLN { get; set; } = 10000.00m;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}
