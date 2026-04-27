using System;
using System.Threading;
using System.Threading.Tasks;
using ForexEnv.Data;
using ForexEnv.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;

namespace ForexEnv
{
    class Program
    {
        private static readonly NbpApiClient _nbpClient = new NbpApiClient();

        static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "8232512714:AAElczhU478fmjvoGOZ3hgQuX7q7NXuf5ek";
            var botClient = new TelegramBotClient(token);

            // Aplikowanie migracji EF Core na starcie (kluczowe dla Railway)
            using (var db = new AppDbContext())
            {
                Console.WriteLine("Sprawdzanie i aplikowanie migracji bazy danych...");
                await db.Database.MigrateAsync();
                Console.WriteLine("Baza danych jest aktualna.");
            }

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMe();
            Console.WriteLine($"Start bota {me.Username} (ForexEnv)");
            
            // W środowisku serwerowym (Docker/Railway) nie używamy Console.ReadLine(), 
            // ponieważ program zamknąłby się natychmiast. Używamy Task.Delay(-1), 
            // aby bot działał bez przerwy.
            await Task.Delay(-1, cts.Token);
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message?.Text != null)
                {
                    await HandleMessageAsync(botClient, update.Message, cancellationToken);
                }
                else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
                {
                    await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wystąpił błąd: {ex.Message}");
            }
        }

        static async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var telegramId = message.From?.Id ?? 0;
            var username = message.From?.Username ?? "Nieznany";
            var text = message.Text;

            using var db = new AppDbContext();
            var user = await db.Users.Include(u => u.Wallets).FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);

            if (user == null && text == "/start")
            {
                user = new Data.User { TelegramId = telegramId, Username = username };
                var wallet = new Wallet { Name = "Główny", BalancePLN = 10000.00m };
                user.Wallets.Add(wallet);
                db.Users.Add(user);
                await db.SaveChangesAsync(cancellationToken);
                
                user.ActiveWalletId = wallet.Id;
                await db.SaveChangesAsync(cancellationToken);

                await botClient.SendMessage(
                    chatId: chatId,
                    text: "Witaj w symulatorze ForexEnv!\nKonto zostało założone pomyślnie. Otrzymujesz 10 000 PLN na start w portfelu 'Główny'.\nKorzystaj z menu poniżej, aby zarządzać swoimi środkami.",
                    replyMarkup: GetMainMenu(),
                    cancellationToken: cancellationToken);
                return;
            }

            if (user == null)
            {
                await botClient.SendMessage(chatId, "Wpisz /start aby założyć konto i rozpocząć grę.", cancellationToken: cancellationToken);
                return;
            }

            var activeWallet = user.Wallets.FirstOrDefault(w => w.Id == user.ActiveWalletId) ?? user.Wallets.FirstOrDefault();
            if (activeWallet != null && user.ActiveWalletId != activeWallet.Id)
            {
                user.ActiveWalletId = activeWallet.Id;
                await db.SaveChangesAsync(cancellationToken);
            }
            if (activeWallet == null) return;

            // Obsługa wpisywania nazwy nowego portfela
            if (user.CurrentState == "WAITING_FOR_WALLET_NAME")
            {
                if (text.StartsWith("/"))
                {
                    user.CurrentState = null;
                    await db.SaveChangesAsync(cancellationToken);
                    await botClient.SendMessage(chatId, "Anulowano tworzenie portfela.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
                    return;
                }

                var newWallet = new Wallet { Name = text, BalancePLN = 10000.00m, UserId = user.Id };
                db.Wallets.Add(newWallet);
                await db.SaveChangesAsync(cancellationToken);

                user.ActiveWalletId = newWallet.Id;
                user.CurrentState = null;
                await db.SaveChangesAsync(cancellationToken);

                await botClient.SendMessage(chatId, $"✅ Utworzono nowy portfel '{text}' i przyznano na start 10 000 PLN.\nAutomatycznie przełączono Cię na ten portfel.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
                return;
            }

            // Obsługa wpisywania własnej ilości (KUPNO)
            if (user.CurrentState != null && user.CurrentState.StartsWith("BUY_CUSTOM_"))
            {
                if (text.StartsWith("/") || text == "❌ Anuluj")
                {
                    user.CurrentState = null;
                    await db.SaveChangesAsync(cancellationToken);
                    await botClient.SendMessage(chatId, "Anulowano.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
                    return;
                }
                string code = user.CurrentState.Split('_')[2];
                if (decimal.TryParse(text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                {
                    user.CurrentState = null;
                    await db.SaveChangesAsync(cancellationToken);
                    try { await BuyCurrencyAsync(botClient, db, chatId, activeWallet, code, amount, cancellationToken); }
                    catch (Exception ex) { await botClient.SendMessage(chatId, $"❌ Błąd: {ex.Message}", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken); }
                }
                else { await botClient.SendMessage(chatId, "Błędna kwota. Podaj liczbę (np. 120.50):"); }
                return;
            }

            // Obsługa wpisywania własnej ilości (SPRZEDAŻ)
            if (user.CurrentState != null && user.CurrentState.StartsWith("SELL_CUSTOM_"))
            {
                if (text.StartsWith("/") || text == "❌ Anuluj")
                {
                    user.CurrentState = null;
                    await db.SaveChangesAsync(cancellationToken);
                    await botClient.SendMessage(chatId, "Anulowano.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
                    return;
                }
                string code = user.CurrentState.Split('_')[2];
                if (decimal.TryParse(text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                {
                    user.CurrentState = null;
                    await db.SaveChangesAsync(cancellationToken);
                    try { await SellCurrencyAsync(botClient, db, chatId, activeWallet, code, amount, cancellationToken); }
                    catch (Exception ex) { await botClient.SendMessage(chatId, $"❌ Błąd: {ex.Message}", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken); }
                }
                else { await botClient.SendMessage(chatId, "Błędna kwota. Podaj liczbę (np. 120.50):"); }
                return;
            }

            // Obsługa klawiatury głównej
            if (text == "/start" || text == "🏠 Menu Główne")
            {
                await botClient.SendMessage(chatId, $"Wybierz opcję z menu poniżej.\nObecny portfel: **{activeWallet.Name}**", replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            else if (text == "➕ Nowy Portfel")
            {
                user.CurrentState = "WAITING_FOR_WALLET_NAME";
                await db.SaveChangesAsync(cancellationToken);
                await botClient.SendMessage(chatId, "Wpisz poniżej nazwę dla Twojego nowego portfela:", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            }
            else if (text == "🗂 Zmień Portfel")
            {
                var buttons = new List<InlineKeyboardButton[]>();
                foreach (var w in user.Wallets)
                {
                    string prefix = w.Id == activeWallet.Id ? "✅ " : "";
                    buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"{prefix}{w.Name}", $"switch_wallet_{w.Id}") });
                }
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Anuluj", "cancel") });

                var inlineKeyboard = new InlineKeyboardMarkup(buttons);
                await botClient.SendMessage(chatId, "Wybierz portfel, na który chcesz się przełączyć:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
            }
            else if (text == "📈 Kursy NBP")
            {
                var rates = await _nbpClient.GetCurrentRatesAsync();
                var topRates = rates.Where(r => new[] { "USD", "EUR", "CHF", "GBP", "JPY", "CAD", "AUD", "SEK" }.Contains(r.Code)).ToList();
                string msg = "📈 Aktualne kursy (NBP):\n\n";
                foreach (var r in topRates) msg += $"🔹 {r.Code}: {r.Mid} PLN\n";
                await botClient.SendMessage(chatId, msg, replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
            }
            else if (text == "💼 Mój Portfel")
            {
                await ShowWalletAsync(botClient, db, chatId, activeWallet, cancellationToken);
            }
            else if (text == "📜 Historia Portfela")
            {
                await ShowHistoryAsync(botClient, db, chatId, activeWallet, cancellationToken);
            }
            else if (text == "🛒 Kup Walutę")
            {
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("💵 USD", "buy_currency_USD"), InlineKeyboardButton.WithCallbackData("💶 EUR", "buy_currency_EUR") },
                    new [] { InlineKeyboardButton.WithCallbackData("💷 GBP", "buy_currency_GBP"), InlineKeyboardButton.WithCallbackData("🏔 CHF", "buy_currency_CHF") },
                    new [] { InlineKeyboardButton.WithCallbackData("💴 JPY", "buy_currency_JPY"), InlineKeyboardButton.WithCallbackData("🍁 CAD", "buy_currency_CAD") },
                    new [] { InlineKeyboardButton.WithCallbackData("🦘 AUD", "buy_currency_AUD"), InlineKeyboardButton.WithCallbackData("🇸🇪 SEK", "buy_currency_SEK") }
                });
                await botClient.SendMessage(chatId, $"Aktywny portfel: {activeWallet.Name}\nWybierz walutę, którą chcesz kupić:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
            }
            else if (text == "💰 Sprzedaj Walutę")
            {
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("💵 USD", "sell_currency_USD"), InlineKeyboardButton.WithCallbackData("💶 EUR", "sell_currency_EUR") },
                    new [] { InlineKeyboardButton.WithCallbackData("💷 GBP", "sell_currency_GBP"), InlineKeyboardButton.WithCallbackData("🏔 CHF", "sell_currency_CHF") },
                    new [] { InlineKeyboardButton.WithCallbackData("💴 JPY", "sell_currency_JPY"), InlineKeyboardButton.WithCallbackData("🍁 CAD", "sell_currency_CAD") },
                    new [] { InlineKeyboardButton.WithCallbackData("🦘 AUD", "sell_currency_AUD"), InlineKeyboardButton.WithCallbackData("🇸🇪 SEK", "sell_currency_SEK") }
                });
                await botClient.SendMessage(chatId, $"Aktywny portfel: {activeWallet.Name}\nWybierz walutę, którą chcesz sprzedać:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendMessage(chatId, "Nie rozumiem tej wiadomości. Wybierz opcję korzystając z przycisków na dole ekranu.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
            }
        }

        static async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var data = callbackQuery.Data;
            var chatId = callbackQuery.Message!.Chat.Id;
            var telegramId = callbackQuery.From.Id;

            using var db = new AppDbContext();
            var user = await db.Users.Include(u => u.Wallets).FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);
            var activeWallet = user?.Wallets.FirstOrDefault(w => w.Id == user.ActiveWalletId) ?? user?.Wallets.FirstOrDefault();
            
            if (activeWallet == null) return;

            if (data != null && data.StartsWith("switch_wallet_"))
            {
                int targetId = int.Parse(data.Split('_')[2]);
                var targetWallet = user!.Wallets.FirstOrDefault(w => w.Id == targetId);
                
                if (targetWallet != null)
                {
                    user.ActiveWalletId = targetWallet.Id;
                    await db.SaveChangesAsync(cancellationToken);
                    try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                    await botClient.SendMessage(chatId, $"✅ Przełączono pomyślnie na portfel: **{targetWallet.Name}**", replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                }
            }
            else if (data != null && data.StartsWith("buy_currency_"))
            {
                string code = data.Split('_')[2];
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("10", $"buy_amount_{code}_10"), InlineKeyboardButton.WithCallbackData("50", $"buy_amount_{code}_50") },
                    new [] { InlineKeyboardButton.WithCallbackData("100", $"buy_amount_{code}_100"), InlineKeyboardButton.WithCallbackData("500", $"buy_amount_{code}_500") },
                    new [] { InlineKeyboardButton.WithCallbackData("✏️ Inna kwota", $"buy_custom_{code}") },
                    new [] { InlineKeyboardButton.WithCallbackData("❌ Anuluj", "cancel") }
                });
                try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                await botClient.SendMessage(chatId, $"KUPUJESZ: {code} na portfel '{activeWallet.Name}'\nWybierz ilość:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
            }
            else if (data != null && data.StartsWith("buy_amount_"))
            {
                var parts = data.Split('_');
                string code = parts[2];
                decimal amount = decimal.Parse(parts[3]);
                
                try
                {
                    try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                    await BuyCurrencyAsync(botClient, db, chatId, activeWallet, code, amount, cancellationToken);
                }
                catch (Exception ex) { await botClient.SendMessage(chatId, $"❌ Błąd: {ex.Message}", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken); }
            }
            else if (data != null && data.StartsWith("buy_custom_"))
            {
                string code = data.Split('_')[2];
                user!.CurrentState = $"BUY_CUSTOM_{code}";
                await db.SaveChangesAsync(cancellationToken);
                try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                await botClient.SendMessage(chatId, $"Wpisz ręcznie ile **{code}** chcesz kupić:", replyMarkup: new ReplyKeyboardRemove(), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            else if (data != null && data.StartsWith("sell_currency_"))
            {
                string code = data.Split('_')[2];
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("10", $"sell_amount_{code}_10"), InlineKeyboardButton.WithCallbackData("50", $"sell_amount_{code}_50") },
                    new [] { InlineKeyboardButton.WithCallbackData("100", $"sell_amount_{code}_100"), InlineKeyboardButton.WithCallbackData("Wszystko (MAX)", $"sell_amount_{code}_ALL") },
                    new [] { InlineKeyboardButton.WithCallbackData("✏️ Inna kwota", $"sell_custom_{code}") },
                    new [] { InlineKeyboardButton.WithCallbackData("❌ Anuluj", "cancel") }
                });
                try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                await botClient.SendMessage(chatId, $"SPRZEDAJESZ: {code} z portfela '{activeWallet.Name}'\nWybierz ilość:", replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
            }
            else if (data != null && data.StartsWith("sell_amount_"))
            {
                var parts = data.Split('_');
                string code = parts[2];
                
                try
                {
                    decimal amount = 0;
                    if (parts[3] == "ALL")
                    {
                        var txs = await db.Transactions.Where(t => t.WalletId == activeWallet.Id && t.CurrencyCode == code).ToListAsync(cancellationToken);
                        amount = txs.Sum(t => t.Type == "BUY" ? t.Amount : -t.Amount);
                        if(amount <= 0) throw new Exception($"Nie posiadasz waluty {code}.");
                    }
                    else
                    {
                        amount = decimal.Parse(parts[3]);
                    }

                    try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                    await SellCurrencyAsync(botClient, db, chatId, activeWallet, code, amount, cancellationToken);
                }
                catch (Exception ex) { await botClient.SendMessage(chatId, $"❌ Błąd: {ex.Message}", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken); }
            }
            else if (data != null && data.StartsWith("sell_custom_"))
            {
                string code = data.Split('_')[2];
                user!.CurrentState = $"SELL_CUSTOM_{code}";
                await db.SaveChangesAsync(cancellationToken);
                try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                await botClient.SendMessage(chatId, $"Wpisz ręcznie ile **{code}** chcesz sprzedać:", replyMarkup: new ReplyKeyboardRemove(), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            else if (data == "cancel")
            {
                try { await botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId, cancellationToken: cancellationToken); } catch { }
                await botClient.SendMessage(chatId, "Anulowano akcję.", replyMarkup: GetMainMenu(), cancellationToken: cancellationToken);
            }

            try { await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken); } catch { }
        }

        static ReplyKeyboardMarkup GetMainMenu()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📈 Kursy NBP", "💼 Mój Portfel", "📜 Historia Portfela" },
                new KeyboardButton[] { "🛒 Kup Walutę", "💰 Sprzedaj Walutę" },
                new KeyboardButton[] { "🗂 Zmień Portfel", "➕ Nowy Portfel" }
            })
            {
                ResizeKeyboard = true
            };
        }

        static async Task ShowWalletAsync(ITelegramBotClient botClient, AppDbContext db, long chatId, Wallet wallet, CancellationToken ct)
        {
            var txs = await db.Transactions.Where(t => t.WalletId == wallet.Id).ToListAsync(ct);
            var holdings = txs.GroupBy(t => t.CurrencyCode)
                              .Select(g => new { 
                                  Code = g.Key, 
                                  Amount = g.Sum(x => x.Type == "BUY" ? x.Amount : -x.Amount) 
                              })
                              .Where(h => h.Amount > 0)
                              .ToList();

            var currentRates = await _nbpClient.GetCurrentRatesAsync();
            decimal totalAssetsValuePLN = 0m;

            string msg = $"💼 Portfel: **{wallet.Name}**\n💵 Wolne środki: {wallet.BalancePLN:F2} PLN\n\nTwoje aktywa walutowe:\n";
            
            if (!holdings.Any()) 
            {
                msg += "Brak obcych walut.\n";
            }
            else
            {
                foreach (var h in holdings) 
                {
                    var rate = currentRates.FirstOrDefault(r => r.Code == h.Code)?.Mid ?? 0m;
                    decimal currentVal = h.Amount * rate;
                    totalAssetsValuePLN += currentVal;
                    msg += $"🔹 {h.Code}: {h.Amount:F2} (Wartość: ~{currentVal:F2} PLN)\n";
                }
            }

            decimal totalWalletValue = wallet.BalancePLN + totalAssetsValuePLN;
            decimal profitOrLoss = totalWalletValue - 10000.00m; // Każdy portfel ma 10k na start
            
            msg += $"\n📊 **Statystyki Portfela:**\n";
            msg += $"Całkowita wartość: {totalWalletValue:F2} PLN\n";
            
            if (profitOrLoss > 0)
                msg += $"Zysk/Strata: 🟢 +{profitOrLoss:F2} PLN\n";
            else if (profitOrLoss < 0)
                msg += $"Zysk/Strata: 🔴 {profitOrLoss:F2} PLN\n";
            else
                msg += $"Zysk/Strata: ⚪ 0.00 PLN\n";

            await botClient.SendMessage(chatId, msg, replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: ct);
        }

        static async Task ShowHistoryAsync(ITelegramBotClient botClient, AppDbContext db, long chatId, Wallet wallet, CancellationToken ct)
        {
            var allTxs = await db.Transactions
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.ExecutedAt)
                .ToListAsync(ct);

            if (!allTxs.Any())
            {
                await botClient.SendMessage(chatId, "Brak historii transakcji w tym portfelu.", replyMarkup: GetMainMenu(), cancellationToken: ct);
                return;
            }

            var recentTxs = allTxs.Take(10).ToList();
            string msg = $"📜 Ostatnie transakcje (Max 10) dla **{wallet.Name}**:\n\n";
            foreach (var tx in recentTxs)
            {
                string icon = tx.Type == "BUY" ? "🟢 KUPNO" : "🔴 SPRZEDAŻ";
                msg += $"{icon} {tx.Amount:F2} {tx.CurrencyCode} po kursie {tx.ExchangeRate:F4}\nData: {tx.ExecutedAt:dd.MM.yyyy HH:mm}\nŁącznie: {tx.TotalCostPLN:F2} PLN\n\n";
            }

            // Generowanie wykresu kołowego aktywów
            var holdings = allTxs.GroupBy(t => t.CurrencyCode)
                                .Select(g => new { Code = g.Key, Amount = g.Sum(x => x.Type == "BUY" ? x.Amount : -x.Amount) })
                                .Where(h => h.Amount > 0)
                                .ToList();

            if (holdings.Any())
            {
                var labels = string.Join(",", holdings.Select(h => $"'{h.Code}'"));
                var data = string.Join(",", holdings.Select(h => h.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                
                string chartConfig = $"{{type:'doughnut',data:{{labels:[{labels}],datasets:[{{data:[{data}]}}]}},options:{{plugins:{{legend:{{position:'right',labels:{{color:'white'}}}},title:{{display:true,text:'Skład portfela walutowego',color:'white'}}}},layout:{{padding:20}}}}}}";
                string chartUrl = "https://quickchart.io/chart?bkg=transparent&c=" + Uri.EscapeDataString(chartConfig);

                try
                {
                    await botClient.SendPhoto(chatId, Telegram.Bot.Types.InputFile.FromUri(chartUrl), caption: msg, replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: ct);
                }
                catch
                {
                    // Fallback jeśli wykres nie zadziała
                    await botClient.SendMessage(chatId, msg, replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: ct);
                }
            }
            else
            {
                await botClient.SendMessage(chatId, msg, replyMarkup: GetMainMenu(), parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
        }

        static async Task BuyCurrencyAsync(ITelegramBotClient botClient, AppDbContext db, long chatId, Wallet wallet, string code, decimal amount, CancellationToken ct)
        {
            if(amount <= 0) throw new Exception("Ilość musi być większa od 0.");
            
            var rate = await _nbpClient.GetRateAsync(code);
            if (rate == null) throw new Exception("Nie znaleziono takiej waluty w tabeli NBP.");

            decimal cost = rate.Mid * amount;
            if (wallet.BalancePLN < cost) throw new Exception($"Niewystarczające środki w portfelu. Koszt to {cost:F2} PLN, posiadasz {wallet.BalancePLN:F2} PLN.");

            wallet.BalancePLN -= cost;
            var tx = new Transaction { WalletId = wallet.Id, CurrencyCode = code, Type = "BUY", Amount = amount, ExchangeRate = rate.Mid, TotalCostPLN = cost };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, $"✅ Sukces! Kupiono {amount} {code} po kursie {rate.Mid} PLN.\nCałkowity koszt: {cost:F2} PLN.", replyMarkup: GetMainMenu(), cancellationToken: ct);
        }

        static async Task SellCurrencyAsync(ITelegramBotClient botClient, AppDbContext db, long chatId, Wallet wallet, string code, decimal amount, CancellationToken ct)
        {
            if(amount <= 0) throw new Exception("Ilość musi być większa od 0.");

            var txs = await db.Transactions.Where(t => t.WalletId == wallet.Id && t.CurrencyCode == code).ToListAsync(ct);
            decimal owned = txs.Sum(t => t.Type == "BUY" ? t.Amount : -t.Amount);
            if (owned < amount) throw new Exception($"Nie posiadasz wystarczającej ilości waluty {code}. Posiadasz tylko: {owned:F2}");

            var rate = await _nbpClient.GetRateAsync(code);
            if (rate == null) throw new Exception("Nie znaleziono takiej waluty w tabeli NBP.");

            decimal revenue = rate.Mid * amount;
            wallet.BalancePLN += revenue;
            var tx = new Transaction { WalletId = wallet.Id, CurrencyCode = code, Type = "SELL", Amount = amount, ExchangeRate = rate.Mid, TotalCostPLN = revenue };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, $"✅ Sukces! Sprzedano {amount} {code} po kursie {rate.Mid} PLN.\nZysk dodany do salda: {revenue:F2} PLN.", replyMarkup: GetMainMenu(), cancellationToken: ct);
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, Telegram.Bot.Polling.HandleErrorSource source, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
    }
}
