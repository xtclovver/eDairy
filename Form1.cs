using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using Timer = System.Windows.Forms.Timer;

namespace eDairy
{
    public partial class Form1 : Form
    {
        // Подключение к БД
        private MySqlConnection connection;
        // Элементы интерфейса
        private ComboBox cbGroup;
        private ComboBox cbMonth;
        private Button btnSyncGroup;
        private DataGridView dgvJournal;
        // Таймер автосохранения
        private Timer autoSaveTimer;
        private bool changesPending = false;

        // Текущий год, выбранный месяц и группа
        private int currentYear = 2025;
        private int currentMonth = 0; // 1-12
        private string currentGroup = "";
        private const int MAX_DAYS = 31; // максимальное число дней

        public Form1()
        {
            InitializeComponent();
            InitializeDatabase();
            LoadGroups(); // загрузка списка групп из master-таблицы
            cbMonth.SelectedIndex = 0; // по умолчанию Январь
            autoSaveTimer = new Timer();
            autoSaveTimer.Interval = 10000; // 10 секунд
            autoSaveTimer.Tick += AutoSaveTimer_Tick;
            autoSaveTimer.Start();
        }

        // Инициализация компонентов (при условии, что designer не генерирует InitializeComponent)
        private void InitializeComponent()
        {
            this.Text = "Электронный дневник оценок";
            this.Width = 1200;
            this.Height = 700;

            Label lblGroup = new Label { Text = "Группа:", Left = 20, Top = 20, AutoSize = true };
            cbGroup = new ComboBox { Left = 100, Top = 20, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            cbGroup.SelectedIndexChanged += CbGroup_SelectedIndexChanged;

            Label lblMonth = new Label { Text = "Месяц:", Left = 280, Top = 20, AutoSize = true };
            cbMonth = new ComboBox { Left = 350, Top = 20, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            cbMonth.Items.AddRange(new string[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" });
            cbMonth.SelectedIndexChanged += CbMonth_SelectedIndexChanged;

            Button btnAddGroup = new Button
            {
                Text = "Создать группу",
                Left = 270, // расположение можно изменить по необходимости
                Top = 20,
                Width = 150
            };
            btnAddGroup.Click += BtnAddGroup_Click;
            this.Controls.Add(btnAddGroup);

            btnSyncGroup = new Button { Text = "Синхронизировать группу", Left = 520, Top = 20, Width = 200 };
            btnSyncGroup.Click += BtnSyncGroup_Click;

            dgvJournal = new DataGridView
            {
                Left = 20,
                Top = 60,
                Width = 1150,
                Height = 550,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvJournal.CellValueChanged += DgvJournal_CellValueChanged;

            this.Controls.Add(lblGroup);
            this.Controls.Add(cbGroup);
            this.Controls.Add(lblMonth);
            this.Controls.Add(cbMonth);
            this.Controls.Add(btnSyncGroup);
            this.Controls.Add(dgvJournal);
        }

        // Инициализация подключения к базе данных
        private void InitializeDatabase()
        {
            string connectionString = "server=localhost;port=3306;user=root;password=123123;database=year_2025;";
            connection = new MySqlConnection(connectionString);
            try
            {
                connection.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка подключения к БД: " + ex.Message);
            }
        }
        private void BtnAddGroup_Click(object sender, EventArgs e)
        {
            string groupName = Microsoft.VisualBasic.Interaction.InputBox("Введите название группы:", "Создать группу", "");

            if (string.IsNullOrWhiteSpace(groupName))
                return;

            for (int month = 1; month <= 12; month++)
            {
                string tableName = $"{groupName}.{month:D2}";
                string createTableQuery = $@"
            CREATE TABLE IF NOT EXISTS `{tableName}` (
                ID INT AUTO_INCREMENT PRIMARY KEY,
                StudentName VARCHAR(255) NOT NULL,
                {GenerateDayColumns()},
                MissedExcused INT DEFAULT 0,
                MissedUnexcused INT DEFAULT 0
            );
        ";

                using (MySqlCommand cmd = new MySqlCommand(createTableQuery, connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Группа успешно создана!");
            LoadGroups(); // Обновить список групп
        }

        // Функция для динамической генерации 31 колонки (Day01, Day02, ..., Day31)
        private string GenerateDayColumns()
        {
            StringBuilder columns = new StringBuilder();
            for (int i = 1; i <= 31; i++)
            {
                columns.Append($"Day{i:D2} VARCHAR(10) DEFAULT NULL, ");
            }
            return columns.ToString().TrimEnd(',', ' ');
        }



        // Загрузка списка групп из master-таблицы (GroupStudents)
        private void LoadGroups()
        {
            cbGroup.Items.Clear();

            string query = "SELECT DISTINCT SUBSTRING_INDEX(table_name, '.', 1) AS group_name " +
                           "FROM information_schema.tables WHERE table_schema = 'year_2025'";

            using (MySqlCommand cmd = new MySqlCommand(query, connection))
            {
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string groupName = reader.GetString(0);
                        if (!cbGroup.Items.Contains(groupName))
                        {
                            cbGroup.Items.Add(groupName);
                        }
                    }
                }
            }

            if (cbGroup.Items.Count > 0)
                cbGroup.SelectedIndex = 0; // Выбрать первую группу
        }


        private void CbGroup_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbGroup.SelectedItem != null)
            {
                currentGroup = cbGroup.SelectedItem.ToString();
                LoadJournal();
            }
        }

        private void CbMonth_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentMonth = cbMonth.SelectedIndex + 1;
            LoadJournal();
        }

        // Загрузка или создание журнала для выбранной группы и месяца
        private void LoadJournal()
        {
            if (string.IsNullOrEmpty(currentGroup) || currentMonth == 0)
                return;
            // Имя таблицы: например, "22ИТ17_1" для января
            string tableName = $"{currentGroup}.{currentMonth:D2}";
            CreateJournalTable(tableName);
            BuildJournalGrid();
            LoadJournalData(tableName);
        }

        // Создание таблицы журнала с колонками: ID, StudentID, FullName, d1...d31
        private void CreateJournalTable(string tableName)
        {
            string columns = "ID INT AUTO_INCREMENT PRIMARY KEY, StudentID INT, FullName VARCHAR(255)";
            for (int i = 1; i <= MAX_DAYS; i++)
            {
                columns += $", d{i} VARCHAR(10)";
            }
            string createQuery = $"CREATE TABLE IF NOT EXISTS `{tableName}` ({columns});";
            MySqlCommand cmd = new MySqlCommand(createQuery, connection);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка создания таблицы журнала: " + ex.Message);
            }
        }

        // Построение DataGridView: первая колонка — ФИО, далее колонки для дней (без воскресений) и вычисляемые колонки
        private void BuildJournalGrid()
        {
            dgvJournal.Columns.Clear();
            // Колонка для ФИО
            DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn { Name = "FullName", HeaderText = "ФИО", ReadOnly = true };
            dgvJournal.Columns.Add(colName);

            int daysInMonth = DateTime.DaysInMonth(currentYear, currentMonth);
            // Добавляем колонки для дней (пропускаем воскресенья)
            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime date = new DateTime(currentYear, currentMonth, day);
                if (date.DayOfWeek == DayOfWeek.Sunday)
                    continue;
                DataGridViewTextBoxColumn colDay = new DataGridViewTextBoxColumn { Name = $"d{day}", HeaderText = day.ToString() };
                dgvJournal.Columns.Add(colDay);
            }
            // Вычисляемые колонки
            DataGridViewTextBoxColumn colAvg = new DataGridViewTextBoxColumn { Name = "Average", HeaderText = "Средняя оценка", ReadOnly = true };
            DataGridViewTextBoxColumn colExcused = new DataGridViewTextBoxColumn { Name = "Excused", HeaderText = "Пропуски (уваж.)", ReadOnly = true };
            DataGridViewTextBoxColumn colUnexcused = new DataGridViewTextBoxColumn { Name = "Unexcused", HeaderText = "Пропуски (неуваж.)", ReadOnly = true };
            dgvJournal.Columns.Add(colAvg);
            dgvJournal.Columns.Add(colExcused);
            dgvJournal.Columns.Add(colUnexcused);
        }

        // Загрузка данных журнала из БД в DataGridView
        private void LoadJournalData(string tableName)
        {
            dgvJournal.Rows.Clear();
            string query = $"SELECT * FROM `{tableName}`";
            MySqlDataAdapter adapter = new MySqlDataAdapter(query, connection);
            DataTable dt = new DataTable();
            try
            {
                adapter.Fill(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки данных журнала: " + ex.Message);
                return;
            }

            // Создаем словарь записей из БД по StudentID
            Dictionary<int, DataRow> journalRows = new Dictionary<int, DataRow>();
            foreach (DataRow row in dt.Rows)
            {
                int studentId = Convert.ToInt32(row["StudentID"]);
                journalRows[studentId] = row;
            }
            // Загружаем мастер-список студентов для выбранной группы
            List<(int id, string fullName)> students = LoadGroupStudents(currentGroup);
            foreach (var student in students)
            {
                DataGridViewRow dgvRow = new DataGridViewRow();
                dgvRow.CreateCells(dgvJournal);
                dgvRow.Cells[0].Value = student.fullName;
                if (journalRows.ContainsKey(student.id))
                {
                    DataRow dbRow = journalRows[student.id];
                    int colIndex = 1;
                    int daysInMonth = DateTime.DaysInMonth(currentYear, currentMonth);
                    for (int day = 1; day <= daysInMonth; day++)
                    {
                        DateTime date = new DateTime(currentYear, currentMonth, day);
                        if (date.DayOfWeek == DayOfWeek.Sunday)
                            continue;
                        dgvRow.Cells[colIndex].Value = dbRow[$"d{day}"];
                        colIndex++;
                    }
                }
                RecalculateRow(dgvRow);
                dgvJournal.Rows.Add(dgvRow);
            }
        }

        // Загрузка студентов из master-таблицы "GroupStudents"
        private List<(int id, string fullName)> LoadGroupStudents(string group)
        {
            List<(int, string)> list = new List<(int, string)>();
            string query = "SELECT ID, FullName FROM GroupStudents WHERE GroupName = @group";
            MySqlCommand cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@group", group);
            try
            {
                MySqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32("ID");
                    string fullName = reader.GetString("FullName");
                    list.Add((id, fullName));
                }
                reader.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки студентов: " + ex.Message);
            }
            return list;
        }

        // Обработчик кнопки синхронизации группы
        private void BtnSyncGroup_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentGroup))
            {
                MessageBox.Show("Выберите группу.");
                return;
            }
            string tableName = $"{currentGroup}_{currentMonth}";
            // Получаем мастер-список студентов
            List<(int id, string fullName)> masterStudents = LoadGroupStudents(currentGroup);
            // Получаем записи журнала из БД
            Dictionary<int, DataRow> journalStudents = new Dictionary<int, DataRow>();
            string query = $"SELECT * FROM `{tableName}`";
            MySqlDataAdapter adapter = new MySqlDataAdapter(query, connection);
            DataTable dt = new DataTable();
            try
            {
                adapter.Fill(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка синхронизации журнала: " + ex.Message);
                return;
            }
            foreach (DataRow row in dt.Rows)
            {
                int studentId = Convert.ToInt32(row["StudentID"]);
                journalStudents[studentId] = row;
            }
            // Добавляем новых студентов
            foreach (var student in masterStudents)
            {
                if (!journalStudents.ContainsKey(student.id))
                {
                    string insertQuery = $"INSERT INTO `{tableName}` (StudentID, FullName) VALUES (@id, @fullName)";
                    MySqlCommand cmd = new MySqlCommand(insertQuery, connection);
                    cmd.Parameters.AddWithValue("@id", student.id);
                    cmd.Parameters.AddWithValue("@fullName", student.fullName);
                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка добавления студента в журнал: " + ex.Message);
                    }
                }
            }
            // Удаляем студентов, которых нет в мастер-списке
            foreach (var kvp in journalStudents)
            {
                if (!masterStudents.Exists(s => s.id == kvp.Key))
                {
                    string deleteQuery = $"DELETE FROM `{tableName}` WHERE StudentID = @id";
                    MySqlCommand cmd = new MySqlCommand(deleteQuery, connection);
                    cmd.Parameters.AddWithValue("@id", kvp.Key);
                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка удаления студента из журнала: " + ex.Message);
                    }
                }
            }
            LoadJournal();
        }

        // При изменении ячейки пересчитываем вычисляемые колонки
        private void DgvJournal_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataGridViewRow row = dgvJournal.Rows[e.RowIndex];
            RecalculateRow(row);
            changesPending = true;
        }

        // Пересчет средней оценки и количества пропусков для строки студента
        private void RecalculateRow(DataGridViewRow row)
        {
            double sum = 0;
            int count = 0;
            int excused = 0;
            int unexcused = 0;

            // Ячейки с оценками находятся со 2-й до предпоследних 3-х колонок
            for (int i = 1; i < row.Cells.Count - 3; i++)
            {
                var val = row.Cells[i].Value;
                if (val != null)
                {
                    string s = val.ToString();
                    if (double.TryParse(s, out double grade))
                    {
                        sum += grade;
                        count++;
                    }
                    else
                    {
                        if (s.ToUpper() == "ПУ")
                            excused++;
                        else if (s.ToUpper() == "ПН")
                            unexcused++;
                    }
                }
            }
            double avg = count > 0 ? sum / count : 0;
            row.Cells[row.Cells.Count - 3].Value = avg.ToString("0.00");
            row.Cells[row.Cells.Count - 2].Value = excused;
            row.Cells[row.Cells.Count - 1].Value = unexcused;
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            if (changesPending)
            {
                SaveJournal();
                changesPending = false;
            }
        }

        // Сохранение данных журнала в БД
        private void SaveJournal()
        {
            if (string.IsNullOrEmpty(currentGroup) || currentMonth == 0)
                return;
            string tableName = $"{currentGroup}_{currentMonth}";
            foreach (DataGridViewRow row in dgvJournal.Rows)
            {
                if (row.IsNewRow) continue;
                string fullName = row.Cells["FullName"].Value?.ToString();
                int studentId = GetStudentIdByFullName(currentGroup, fullName);
                if (studentId == 0)
                    continue;
                // Формируем запрос для обновления ячеек d1...d31
                string updateQuery = $"UPDATE `{tableName}` SET ";
                int daysInMonth = DateTime.DaysInMonth(currentYear, currentMonth);
                List<string> setClauses = new List<string>();
                int colIndex = 1;
                for (int day = 1; day <= daysInMonth; day++)
                {
                    DateTime date = new DateTime(currentYear, currentMonth, day);
                    if (date.DayOfWeek == DayOfWeek.Sunday)
                        continue;
                    string colName = $"d{day}";
                    setClauses.Add($"{colName} = @val{day}");
                    colIndex++;
                }
                updateQuery += string.Join(", ", setClauses) + " WHERE StudentID = @id";
                MySqlCommand cmd = new MySqlCommand(updateQuery, connection);
                colIndex = 1;
                for (int day = 1; day <= daysInMonth; day++)
                {
                    DateTime date = new DateTime(currentYear, currentMonth, day);
                    if (date.DayOfWeek == DayOfWeek.Sunday)
                        continue;
                    var val = row.Cells[colIndex].Value;
                    cmd.Parameters.AddWithValue($"@val{day}", val == null ? DBNull.Value : val);
                    colIndex++;
                }
                cmd.Parameters.AddWithValue("@id", studentId);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сохранения данных: " + ex.Message);
                }
            }
        }

        // Получение StudentID по ФИО из master-таблицы для данной группы
        private int GetStudentIdByFullName(string group, string fullName)
        {
            string query = "SELECT ID FROM GroupStudents WHERE GroupName = @group AND FullName = @fullName LIMIT 1";
            MySqlCommand cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@group", group);
            cmd.Parameters.AddWithValue("@fullName", fullName);
            try
            {
                object result = cmd.ExecuteScalar();
                if (result != null)
                    return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка поиска студента: " + ex.Message);
            }
            return 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveJournal();
            connection.Close();
            base.OnFormClosing(e);
        }
    }
}
