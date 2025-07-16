using Microsoft.VisualBasic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Diplom1
{
    internal class Program
    {
        private static ITelegramBotClient botTocken = new TelegramBotClient("7451931591:AAEEW7SN84jUw2u-Jl29Aa2i36mtqksCLXA");
        private static Dictionary<long, string> userStates = new Dictionary<long, string>();
        private static Dictionary<long, DateTime> tempReminderDates = new Dictionary<long, DateTime>();

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(update));

            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(botClient, update.CallbackQuery);
                return;
            }

            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
            {
                Message message = update.Message;
                UserInfo userInfo;
                string messageTextForSwitch = GetWordByIndex(message.Text, 0);

                switch (messageTextForSwitch.ToLower())
                {
                    case "/start":
                        if (!CheckUser(message))
                        {
                            AddUser(message);
                            await botClient.SendTextMessageAsync(message.Chat, "Вы добавлены в базу данных. Добро пожаловать!");
                        }

                        var startKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("Мои заметки", "myNotes"),
                                InlineKeyboardButton.WithCallbackData("Показать заметку", "showNote")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("Редактировать заметку", "editNote"),
                                InlineKeyboardButton.WithCallbackData("Напоминание", "reminder")
                            }
                        });

                        await botClient.SendTextMessageAsync(
                            message.Chat,
                            "Добро пожаловать! Выберите действие:",
                            replyMarkup: startKeyboard);
                        return;

                    case "/reminder":
                        var now = DateTime.Now;
                        await SendCalendar(message.Chat.Id, now.Year, now.Month);
                        return;

                    case "/cancel":
                        if (userStates.ContainsKey(message.Chat.Id))
                        {
                            userStates.Remove(message.Chat.Id);
                        }
                        if (tempReminderDates.ContainsKey(message.Chat.Id))
                        {
                            tempReminderDates.Remove(message.Chat.Id);
                        }
                        await botClient.SendTextMessageAsync(message.Chat, "Действие отменено");
                        return;
                }

                // Обработка состояния пользователя
                if (userStates.ContainsKey(message.Chat.Id))
                {
                    string state = userStates[message.Chat.Id];
                    if (state.StartsWith("editNote_"))
                    {
                        int noteNumber = int.Parse(state.Split('_')[1]);
                        EditNote(message.Chat.Id, noteNumber, message.Text);
                        userStates.Remove(message.Chat.Id);
                        await botClient.SendTextMessageAsync(message.Chat, $"Заметка {noteNumber} успешно обновлена!");
                        return;
                    }
                    else if (state == "showNote")
                    {
                        int noteNumber;
                        if (int.TryParse(message.Text, out noteNumber))
                        {
                            if (noteNumber < 1 || noteNumber > 4)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Некорректный номер заметки. Введите число от 1 до 4.");
                                return;
                            }

                            userInfo = GetUserInfo(message);
                            string noteText = "";
                            switch (noteNumber)
                            {
                                case 1: noteText = userInfo.Note1; break;
                                case 2: noteText = userInfo.Note2; break;
                                case 3: noteText = userInfo.Note3; break;
                                case 4: noteText = userInfo.Note4; break;
                            }

                            await botClient.SendTextMessageAsync(message.Chat, $"Заметка {noteNumber}:\n{noteText}");
                            userStates.Remove(message.Chat.Id);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Пожалуйста, введите номер заметки (1-4):");
                        }
                        return;
                    }
                    else if (state == "waiting_reminder_text")
                    {
                        // Сохраняем напоминание с выбранной датой и текстом
                        if (tempReminderDates.ContainsKey(message.Chat.Id))
                        {
                            DateTime selectedDateTime = tempReminderDates[message.Chat.Id];
                            string reminderText = message.Text;

                            // Проверяем, не прошла ли уже дата
                            if (selectedDateTime < DateTime.Now)
                            {
                                Console.WriteLine(selectedDateTime);
                                Console.WriteLine(DateTime.Now);
                                await botClient.SendTextMessageAsync(message.Chat, "Указанное время уже прошло");
                            }
                            else
                            {
                                // Сохраняем в формате "HH:mm yyyy-MM-dd текст"
                                string formattedReminder = $"{selectedDateTime:HH:mm} {selectedDateTime:yyyy-MM-dd} {reminderText}";
                                EditReminder(message.Chat.Id, formattedReminder);
                                await botClient.SendTextMessageAsync(message.Chat, $"✅ Напоминание установлено на {selectedDateTime:HH:mm} ({selectedDateTime:dd.MM.yyyy})");
                            }

                            userStates.Remove(message.Chat.Id);
                            tempReminderDates.Remove(message.Chat.Id);
                        }
                        return;
                    }
                }
            }
        }

        private static async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            long chatId = callbackQuery.Message.Chat.Id;
            string data = callbackQuery.Data;

            try
            {
                switch (data)
                {
                    case "myNotes":
                        UserInfo userInfo = GetUserInfo(callbackQuery.Message);

                        string note1 = string.IsNullOrEmpty(userInfo.Note1) ? "пусто" : GetFirstWords(userInfo.Note1, 5);
                        string note2 = string.IsNullOrEmpty(userInfo.Note2) ? "пусто" : GetFirstWords(userInfo.Note2, 5);
                        string note3 = string.IsNullOrEmpty(userInfo.Note3) ? "пусто" : GetFirstWords(userInfo.Note3, 5);
                        string note4 = string.IsNullOrEmpty(userInfo.Note4) ? "пусто" : GetFirstWords(userInfo.Note4, 5);

                        await botClient.SendTextMessageAsync(
                            chatId,
                            $"Ваши заметки:\n\n" +
                            $"1️⃣ {note1}\n\n" +
                            $"2️⃣ {note2}\n\n" +
                            $"3️⃣ {note3}\n\n" +
                            $"4️⃣ {note4}");
                        break;

                    case "showNote":
                        var noteSelectionKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("1", "show_note_1"),
                                InlineKeyboardButton.WithCallbackData("2", "show_note_2")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("3", "show_note_3"),
                                InlineKeyboardButton.WithCallbackData("4", "show_note_4")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("Отмена", "cancel")
                            }
                        });

                        await botClient.SendTextMessageAsync(
                            chatId,
                            "Выберите номер заметки:",
                            replyMarkup: noteSelectionKeyboard);
                        break;

                    case "editNote":
                        var editNoteKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("1", "edit_note_1"),
                                InlineKeyboardButton.WithCallbackData("2", "edit_note_2")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("3", "edit_note_3"),
                                InlineKeyboardButton.WithCallbackData("4", "edit_note_4")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("Отмена", "cancel")
                            }
                        });

                        await botClient.SendTextMessageAsync(
                            chatId,
                            "Выберите заметку для редактирования:",
                            replyMarkup: editNoteKeyboard);
                        break;

                    case "reminder":
                        var now = DateTime.Now;
                        await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);
                        await SendCalendar(chatId, now.Year, now.Month);
                        break;

                    case "cancel":
                        await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);
                        if (userStates.ContainsKey(chatId))
                        {
                            userStates.Remove(chatId);
                        }
                        if (tempReminderDates.ContainsKey(chatId))
                        {
                            tempReminderDates.Remove(chatId);
                        }
                        await botClient.SendTextMessageAsync(chatId, "Действие отменено");
                        break;

                    default:
                        if (data.StartsWith("show_note_"))
                        {
                            int noteNumber = int.Parse(data.Split('_')[2]);
                            userInfo = GetUserInfo(callbackQuery.Message);
                            string noteText = "";

                            switch (noteNumber)
                            {
                                case 1: noteText = userInfo.Note1; break;
                                case 2: noteText = userInfo.Note2; break;
                                case 3: noteText = userInfo.Note3; break;
                                case 4: noteText = userInfo.Note4; break;
                            }

                            if (string.IsNullOrEmpty(noteText))
                            {
                                noteText = "Заметка пуста";
                            }

                            await botClient.SendTextMessageAsync(chatId, $"📝 Заметка {noteNumber}:\n\n{noteText}");
                        }
                        else if (data.StartsWith("edit_note_"))
                        {
                            int noteNumber = int.Parse(data.Split('_')[2]);

                            if (userStates.ContainsKey(chatId))
                            {
                                userStates[chatId] = $"editNote_{noteNumber}";
                            }
                            else
                            {
                                userStates.Add(chatId, $"editNote_{noteNumber}");
                            }

                            await botClient.SendTextMessageAsync(
                                chatId,
                                $"Введите новый текст для заметки {noteNumber}:\n" +
                                "(или введите /cancel для отмены)");
                        }
                        else if (data.StartsWith("select_day_"))
                        {
                            var parts = data.Split('_');
                            int year = int.Parse(parts[2]);
                            int month = int.Parse(parts[3]);
                            int day = int.Parse(parts[4]);

                            tempReminderDates[chatId] = new DateTime(year, month, day);
                            await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);
                            await SendHourSelection(chatId);
                        }
                        else if (data.StartsWith("select_hour_"))
                        {
                            int hour = int.Parse(data.Split('_')[2]);
                            var currentDate = tempReminderDates[chatId];
                            tempReminderDates[chatId] = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day, hour, 0, 0);

                            await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);
                            await SendMinuteSelection(chatId, hour);
                        }
                        else if (data.StartsWith("select_minute_"))
                        {
                            var parts = data.Split('_');
                            int hour = int.Parse(parts[2]);
                            int minute = int.Parse(parts[3]);

                            var currentDate = tempReminderDates[chatId];
                            var fullDateTime = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day, hour, minute, 0);

                            // Удаляем сообщение с клавиатурой в любом случае
                            await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);

                            if (fullDateTime < DateTime.Now)
                            {
                                await botClient.SendTextMessageAsync(chatId, "Указанное время уже прошло");
                                tempReminderDates.Remove(chatId);
                            }
                            else
                            {
                                // Сохраняем полную дату с минутами
                                tempReminderDates[chatId] = fullDateTime;
                                userStates[chatId] = "waiting_reminder_text";
                                await botClient.SendTextMessageAsync(
                                    chatId,
                                    $"Вы выбрали: {hour:00}:{minute:00} ({fullDateTime:dd.MM.yyyy})\n\n" +
                                    "Теперь введите текст напоминания:");
                            }
                        }
                        else if (data.StartsWith("prev_month_") || data.StartsWith("next_month_"))
                        {
                            var parts = data.Split('_');
                            int year = int.Parse(parts[2]);
                            int month = int.Parse(parts[3]);

                            if (data.StartsWith("prev_month_"))
                            {
                                month--;
                                if (month < 1)
                                {
                                    month = 12;
                                    year--;
                                }
                            }
                            else
                            {
                                month++;
                                if (month > 12)
                                {
                                    month = 1;
                                    year++;
                                }
                            }

                            await DeleteMessageWithKeyboard(botClient, chatId, callbackQuery.Message.MessageId);
                            await SendCalendar(chatId, year, month);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки callback: {ex.Message}");
            }
            finally
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
            }
        }

        // Методы для работы с календарем
        private static async Task SendCalendar(long chatId, int year, int month)
        {
            var monthNames = new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };

            var headerButtons = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("<", $"prev_month_{year}_{month}"),
                    InlineKeyboardButton.WithCallbackData($"{monthNames[month - 1]} {year}", "ignore"),
                    InlineKeyboardButton.WithCallbackData(">", $"next_month_{year}_{month}")
                }
            };

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var firstDayOfMonth = new DateTime(year, month, 1);
            var dayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            var dayButtons = new List<InlineKeyboardButton[]>();
            var currentRow = new List<InlineKeyboardButton>();

            for (int i = 0; i < dayOfWeek; i++)
            {
                currentRow.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                currentRow.Add(InlineKeyboardButton.WithCallbackData(day.ToString(), $"select_day_{year}_{month}_{day}"));

                if (currentRow.Count >= 7)
                {
                    dayButtons.Add(currentRow.ToArray());
                    currentRow = new List<InlineKeyboardButton>();
                }
            }

            if (currentRow.Count > 0)
            {
                while (currentRow.Count < 7)
                {
                    currentRow.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));
                }
                dayButtons.Add(currentRow.ToArray());
            }

            var allButtons = new List<InlineKeyboardButton[]>();
            allButtons.AddRange(headerButtons);
            allButtons.AddRange(dayButtons);

            var inlineKeyboard = new InlineKeyboardMarkup(allButtons);

            await botTocken.SendTextMessageAsync(
                chatId: chatId,
                text: "📅 Выберите дату:",
                replyMarkup: inlineKeyboard);
        }

        private static async Task SendHourSelection(long chatId)
        {
            var hourButtons = new List<InlineKeyboardButton[]>();

            for (int i = 0; i < 6; i++)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 4; j++)
                {
                    int hour = i * 4 + j;
                    if (hour < 24)
                    {
                        row.Add(InlineKeyboardButton.WithCallbackData(hour.ToString("00"), $"select_hour_{hour}"));
                    }
                }
                hourButtons.Add(row.ToArray());
            }

            var inlineKeyboard = new InlineKeyboardMarkup(hourButtons);

            await botTocken.SendTextMessageAsync(
                chatId: chatId,
                text: "⏰ Выберите час:",
                replyMarkup: inlineKeyboard);
        }

        private static async Task SendMinuteSelection(long chatId, int hour)
        {
            var minuteButtons = new List<InlineKeyboardButton[]>();

            for (int i = 0; i < 9; i++)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 7; j++)
                {
                    int minute = i * 7 + j;
                    if (minute < 60)
                    {
                        row.Add(InlineKeyboardButton.WithCallbackData(minute.ToString("00"), $"select_minute_{hour}_{minute}"));
                    }
                }
                minuteButtons.Add(row.ToArray());
            }

            var inlineKeyboard = new InlineKeyboardMarkup(minuteButtons);

            await botTocken.SendTextMessageAsync(
                chatId: chatId,
                text: "⏱️ Выберите минуты:",
                replyMarkup: inlineKeyboard);
        }

        private static async Task DeleteMessageWithKeyboard(ITelegramBotClient botClient, long chatId, int messageId)
        {
            try
            {
                await botClient.DeleteMessageAsync(chatId, messageId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось удалить сообщение: {ex.Message}");
            }
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) =>
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));

        static void Main(string[] args)
        {
            Console.WriteLine("Запущен бот " + botTocken.GetMeAsync().Result.FirstName);

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }
            };

            botTocken.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );

            Thread.Sleep(TimeSpan.FromSeconds(60 - DateTime.Now.Second));
            CheckTimerForReminder();

            Console.ReadLine();
        }

        private static void CheckTimerForReminder()
        {
            DateTime now = DateTime.Now.Date;
            List<UserInfo> userInfoList = GetUsersInfo();

            foreach (UserInfo userInfo in userInfoList)
            {
                if (!string.IsNullOrEmpty(userInfo.Reminder))
                {
                    string[] reminderParts = userInfo.Reminder.Split(' ');
                    if (reminderParts.Length < 2) continue;

                    try
                    {
                        DateTime time = DateTime.ParseExact(reminderParts[0], "HH:mm", CultureInfo.InvariantCulture);

                        if (DateTime.Now.Hour == time.Hour &&
                            DateTime.Now.Minute == time.Minute &&
                            DateTime.Now.Date >= time.Date)
                        {
                            string userMessage = string.Join(" ", reminderParts.Skip(1));
                            ThrowMessage(userInfo.UserTGId, $"⏰ Напоминание:\n\n{userMessage}");
                            EditReminder(userInfo.UserTGId, "");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка обработки напоминания: {ex.Message}");
                    }
                }
            }

            Thread.Sleep(TimeSpan.FromMinutes(1));
            CheckTimerForReminder();
        }

        private static async void ThrowMessage(long userTGID, string message)
        {
            await botTocken.SendTextMessageAsync(userTGID, message);
        }

        #region[Взаимодействие с БД]
        static string connectionString = "Data Source=|DataDirectory|\\DiplomBD.db;";

        private struct UserInfo
        {
            public long UserTGId;
            public string Note1;
            public string Note2;
            public string Note3;
            public string Note4;
            public string Reminder;

            public UserInfo(long userTGId, string note1, string note2, string note3, string note4, string reminder)
            {
                UserTGId = userTGId;
                Note1 = note1;
                Note2 = note2;
                Note3 = note3;
                Note4 = note4;
                Reminder = reminder;
            }
        }

        static void AddUser(Message message)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string insertQuery = $"INSERT INTO MainTable (UserTGID) VALUES ({message.Chat.Id});";
                using (var command = new SQLiteCommand(insertQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        static void EditNote(long chatId, int noteNumber, string noteText)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string updateQuery = $"UPDATE MainTable SET Note{noteNumber} = @noteText WHERE UserTGID = {chatId};";
                using (var command = new SQLiteCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@noteText", noteText);
                    command.ExecuteNonQuery();
                }
            }
        }

        static void EditReminder(long userTGID, string reminderText)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string updateQuery = $"UPDATE MainTable SET Reminder = @reminderText WHERE UserTGID = {userTGID};";
                using (var command = new SQLiteCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@reminderText", reminderText);
                    command.ExecuteNonQuery();
                }
            }
        }

        static bool CheckUser(Message message)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = $"SELECT COUNT(*) FROM MainTable WHERE UserTGID = {message.Chat.Id};";
                using (var command = new SQLiteCommand(query, connection))
                {
                    long count = (long)command.ExecuteScalar();
                    return count > 0;
                }
            }
        }

        static UserInfo GetUserInfo(Message message)
        {
            return GetUserInfoByChatId(message.Chat.Id);
        }

        static UserInfo GetUserInfoByChatId(long chatId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = $"SELECT * FROM MainTable WHERE UserTGID = {chatId}";
                using (var command = new SQLiteCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new UserInfo(
                                long.Parse(reader["UserTGID"].ToString()),
                                reader["Note1"].ToString(),
                                reader["Note2"].ToString(),
                                reader["Note3"].ToString(),
                                reader["Note4"].ToString(),
                                reader["Reminder"].ToString()
                            );
                        }
                        return new UserInfo();
                    }
                }
            }
        }

        static List<UserInfo> GetUsersInfo()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = $"SELECT * FROM MainTable";
                using (var command = new SQLiteCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        List<UserInfo> userInfoList = new List<UserInfo>();
                        while (reader.Read())
                        {
                            userInfoList.Add(new UserInfo(
                                long.Parse(reader["UserTGID"].ToString()),
                                reader["Note1"].ToString(),
                                reader["Note2"].ToString(),
                                reader["Note3"].ToString(),
                                reader["Note4"].ToString(),
                                reader["Reminder"].ToString()
                            ));
                        }
                        return userInfoList;
                    }
                }
            }
        }
        #endregion

        #region[Вспомогательные функции]
        static string GetWordByIndex(string str, int index)
        {
            if (string.IsNullOrWhiteSpace(str))
                return string.Empty;

            string[] words = str.Split(new char[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (index >= 0 && index < words.Length)
                return words[index];

            return string.Empty;
        }

        static string RemoveWords(string input, int count)
        {
            string[] words = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= count)
                return string.Empty;

            return string.Join(" ", words.Skip(count));
        }

        static string GetFirstWords(string input, int wordCount)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            string[] words = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= wordCount)
                return input;

            return string.Join(" ", words.Take(wordCount)) + "...";
        }
        #endregion
    }
}