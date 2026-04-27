using System;
using System.Collections.Generic;

namespace ForexEnv.Data
{
    public class User
    {
        public int Id { get; set; }
        public long TelegramId { get; set; }
        public string? Username { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ActiveWalletId { get; set; }
        public string? CurrentState { get; set; }

        public ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
    }
}
