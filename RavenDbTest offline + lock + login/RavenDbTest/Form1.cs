using Microsoft.VisualBasic.ApplicationServices;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using BCryptNet = BCrypt.Net.BCrypt;


namespace RavenDbTest
{
    public partial class Form1 : Form
    {
        string databaseFilePath = @"C:\Database\LocalData.db";
        private IDocumentStore store;
        private System.Windows.Forms.Timer onlineCheckTimer;
        bool isUserAuthenticated = false;

        public Form1()
        {
            InitializeComponent();

            timer1.Start();
            this.MouseMove += Form1_Activity;
            this.KeyDown += Form1_Activity;
            timer1.Interval = 5000;


            // Ensure the directory exists
            string databaseDirectory = Path.GetDirectoryName(databaseFilePath);
            if (!Directory.Exists(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            X509Certificate2 certificate = new X509Certificate2("C:\\Users\\OMRDev\\Desktop\\RavenDB\\Server\\cluster.server.certificate.photographerapp.pfx");
            store = new DocumentStore
            {
                Urls = new[] { "https://a.photographerapp.ravendb.community" },
                Database = "users",
                Certificate = certificate,
            }.Initialize();

            // Ensure the local SQLite database and table are created
            using (var connection = new SQLiteConnection($"Data Source={databaseFilePath}"))
            {
                connection.Open();

                using (var command = new SQLiteCommand("CREATE TABLE IF NOT EXISTS Products (Id TEXT PRIMARY KEY, Name TEXT, IsSynchronized INTEGER)", connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            // Initialize the timer to check online status every 10 seconds
            onlineCheckTimer = new System.Windows.Forms.Timer();
            onlineCheckTimer.Interval = 10000; // 10 seconds
            onlineCheckTimer.Tick += OnlineCheckTimer_Tick;
            onlineCheckTimer.Start();

            // Check for unsynchronized data and synchronize if needed
            //SynchronizeUnsyncedData();
        }

        private void OnlineCheckTimer_Tick(object sender, EventArgs e)
        {
            // Periodically check if online and synchronize if needed
            SynchronizeUnsyncedData();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (isUserAuthenticated)
            {
                using (IDocumentSession session = store.OpenSession())
                {
                    Product product = new Product
                    {
                        Id = Guid.NewGuid().ToString(), // Set a unique identifier (e.g., GUID)
                        Name = "Mihai",
                    };

                    // Save to local SQLite database
                    SaveToLocalDatabase(product);

                    MessageBox.Show("It works");

                    // Mark as unsynchronized
                    product.IsSynchronized = false;

                    // If online, sync with RavenDB
                    if (IsOnline())
                    {
                        session.Store(product);
                        product.IsSynchronized = true;
                    }

                    session.SaveChanges();
                }
            }
            else
            {
                MessageBox.Show("You need to be authenticated first");
            }

        }

        private void SaveToLocalDatabase(Product product)
        {
            using (var connection = new SQLiteConnection($"Data Source={databaseFilePath}"))
            {
                connection.Open();

                // Check if the product already exists in the local database
                using (var checkCommand = new SQLiteCommand("SELECT COUNT(*) FROM Products WHERE Id = @Id", connection))
                {
                    checkCommand.Parameters.AddWithValue("@Id", product.Id);
                    int count = Convert.ToInt32(checkCommand.ExecuteScalar());

                    if (count == 0)
                    {
                        // Product does not exist, insert it
                        using (var insertCommand = new SQLiteCommand("INSERT INTO Products (Id, Name, IsSynchronized) VALUES (@Id, @Name, @IsSynchronized)", connection))
                        {
                            insertCommand.Parameters.AddWithValue("@Id", product.Id);
                            insertCommand.Parameters.AddWithValue("@Name", product.Name);
                            insertCommand.Parameters.AddWithValue("@IsSynchronized", product.IsSynchronized ? 1 : 0);

                            insertCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        private bool IsOnline()
        {
            try
            {
                return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SynchronizeUnsyncedData()
        {
            if (IsOnline())
            {
                using (IDocumentSession session = store.OpenSession())
                {
                    using (var connection = new SQLiteConnection($"Data Source={databaseFilePath}"))
                    {
                        connection.Open();

                        using (var transaction = connection.BeginTransaction())
                        {
                            // Select unsynchronized items
                            using (var command = new SQLiteCommand("SELECT * FROM Products WHERE IsSynchronized = 0", connection))
                            {
                                using (var reader = command.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        // Create a Product object from the local SQLite data
                                        var product = new Product
                                        {
                                            Id = reader["Id"].ToString(),
                                            Name = reader["Name"].ToString(),
                                            IsSynchronized = false, // Will be updated during synchronization
                                        };

                                        // Save the product to RavenDB
                                        session.Store(product);
                                        session.SaveChanges(); // Save changes to RavenDB

                                        // Mark the record as synchronized in the local SQLite database
                                        using (var updateCommand = new SQLiteCommand("UPDATE Products SET IsSynchronized = 1 WHERE Id = @Id", connection))
                                        {
                                            updateCommand.Parameters.AddWithValue("@Id", product.Id);
                                            updateCommand.ExecuteNonQuery();
                                        }
                                    }
                                }
                            }

                            transaction.Commit(); // Commit the transaction
                        }
                    }
                }
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            ShowLockScreen();
        }

        private void ShowLockScreen()
        {
            // Stop the timer to avoid multiple lock screen displays
            timer1.Stop();

            // Create an instance of LockScreenForm
            LockScreenForm lockScreen = new LockScreenForm();

            // Show the lock screen form as a dialog
            DialogResult result = lockScreen.ShowDialog();

            // Check if the user successfully unlocked the screen
            if (result == DialogResult.OK)
            {
                // User unlocked the screen, reset the timer
                timer1.Start();
            }
            else
            {
                // User closed the lock screen without unlocking, exit the application or take appropriate action
                Close();
            }
        }

        private void Form1_Activity(object sender, EventArgs e)
        {
            // Reset the inactivity timer
            timer1.Stop();
            timer1.Start();
        }

        private string HashPassword(string password)
        {
            return BCryptNet.HashPassword(password, BCryptNet.GenerateSalt());
        }

        private void RegisterUser(string username, string password)
        {
            if (IsOnline())
            {
                using (var session = store.OpenSession())
                {
                    // Check if the username is already taken
                    if (session.Query<User>().Any(u => u.Username == username))
                    {
                        MessageBox.Show("Username already exists. Please choose a different one.");
                        return;
                    }

                    // Create a new user and store it in RavenDB
                    var newUser = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Username = username,
                        PasswordHash = HashPassword(password),
                    };

                    session.Store(newUser);
                    session.SaveChanges();

                    MessageBox.Show("Registration successful!");
                }
            }
            else
            {
                MessageBox.Show("You dont't have internet connection!");
            }

        }

        private bool AuthenticateUser(string username, string password)
        {
            if (IsOnline())
            {
                using (var session = store.OpenSession())
                {
                    var user = session.Query<User>().FirstOrDefault(u => u.Username == username);

                    if (user != null && BCryptNet.Verify(password, user.PasswordHash))
                    {
                        isUserAuthenticated = true;
                        MessageBox.Show("Login successful!");
                        return true;
                    }
                    else
                    {
                        MessageBox.Show("Invalid credentials");
                        return false;
                    }
                }
            }
            else
            {
                MessageBox.Show("You dont't have internet connection!");
                return false;
            }
        }

        private void Register_Click(object sender, EventArgs e)
        {
            string username = textBox1.Text;
            string password = textBox2.Text;

            RegisterUser(username, password);
        }

        private void Login_Click(object sender, EventArgs e)
        {
            string username = textBox1.Text;
            string password = textBox2.Text;

            AuthenticateUser(username, password);
        }
    }
}
