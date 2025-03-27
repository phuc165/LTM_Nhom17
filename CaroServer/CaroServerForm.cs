﻿using System;
using System.Drawing;
using System.Windows.Forms;

namespace CaRoServer
{
    public class CaroServer : Form
    {
        private Server server;
        private Button startButton;
        private Label statusLabel;

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
                Location = new Point(20, 50),
                Size = new Size(100, 30)
            };
            startButton.Click += StartServer_Click;

            this.Controls.Add(statusLabel);
            this.Controls.Add(startButton);

            this.FormClosing += (s, e) => server?.Stop();
        }

        private void StartServer_Click(object sender, EventArgs e)
        {
            if (server == null)
            {
                server = new Server(UpdateStatus);
                server.Start();
                startButton.Text = "Stop Server";
            }
            else
            {
                server.Stop();
                server = null;
                startButton.Text = "Start Server";
            }
        }

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