using System;
using System.Drawing;
using System.Windows.Forms;

namespace CaroServer
{
    public class CaroServer : Form
    {
        private Server server;
        private Button startButton;
        private Label statusLabel;
        private TextBox passwordTextBox;
        private Label passwordLabel;
        private string serverPassword;

        public CaroServer()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Caro Game Server";
            this.Size = new Size(400, 300);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            statusLabel = new Label
            {
                Text = "Server Status: Not Running",
                Location = new Point(20, 20),
                AutoSize = true
            };

            startButton = new Button
            {
                Text = "Start Server",
                Location = new Point(20, 120),
                Size = new Size(100, 30)
            };
            startButton.Click += StartServer_Click;

            passwordLabel = new Label
            {
                Text = "Server Password:",
                Location = new Point(20, 50),
                AutoSize = true
            };

            passwordTextBox = new TextBox
            {
                Location = new Point(20, 80),
                Size = new Size(200, 25),
                PasswordChar = '*'
            };

            this.Controls.Add(statusLabel);
            this.Controls.Add(startButton);
            this.Controls.Add(passwordLabel);
            this.Controls.Add(passwordTextBox);

            // Remove server.Stop() from FormClosing, and handle it manually
            this.FormClosing += (s, e) =>
            {
                // Ask user if they really want to close the application
                if (server != null && !ConfirmServerStop())
                {
                    e.Cancel = true; // Cancel closing the form
                }
            };
        }

        private void StartServer_Click(object sender, EventArgs e)
        {
            if (server == null)
            {
                serverPassword = passwordTextBox.Text.Trim();

                if (string.IsNullOrEmpty(serverPassword))
                {
                    MessageBox.Show("Please set a password for the server.");
                    return;
                }

                // Pass the UpdateStatus method to the server
                server = new Server(UpdateStatus, serverPassword);
                server.Start();
                startButton.Text = "Stop Server";
            }
            else
            {
                server.Stop();
                server = null;
                startButton.Text = "Start Server";
                passwordTextBox.Text = "";
            }
        }

        private bool ConfirmServerStop()
        {
            var result = MessageBox.Show("Are you sure you want to stop the server?", "Confirm Stop",
                                         MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }

        // Update UI status on the main thread
        private void UpdateStatus(string status)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action<string>(UpdateStatus), status);
            }
            else
            {
                statusLabel.Text = status;
            }
        }
    }
}
