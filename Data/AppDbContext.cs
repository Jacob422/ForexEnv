using System;
using Microsoft.EntityFrameworkCore;

namespace ForexEnv.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            string connectionString;

            if (string.IsNullOrEmpty(databaseUrl))
            {
                // Fallback dla lokalnego developmentu
                connectionString = "Host=localhost;Database=forexenv;Username=postgres;Password=postgres";
            }
            else
            {
                // Przekształcenie DATABASE_URL (URI) z Railway na poprawny Connection String dla Npgsql
                bool isUri = Uri.TryCreate(databaseUrl, UriKind.Absolute, out Uri dbUri);
                if (isUri && dbUri.Scheme == "postgresql")
                {
                    var userInfo = dbUri.UserInfo.Split(':');
                    connectionString = $"Host={dbUri.Host};Port={dbUri.Port};Database={dbUri.LocalPath.Substring(1)};Username={userInfo[0]};Password={userInfo[1]};SslMode=Disable;";
                }
                else
                {
                    connectionString = databaseUrl; // Zakładamy, że podano standardowy Npgsql connection string
                }
            }

            optionsBuilder.UseNpgsql(connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Konfiguracja precyzji dla walut (aby uniknąć problemów z ułamkami)
            modelBuilder.Entity<Wallet>().Property(w => w.BalancePLN).HasPrecision(18, 2);
            modelBuilder.Entity<Transaction>().Property(t => t.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Transaction>().Property(t => t.ExchangeRate).HasPrecision(18, 4);
            modelBuilder.Entity<Transaction>().Property(t => t.TotalCostPLN).HasPrecision(18, 2);
        }
    }
}
