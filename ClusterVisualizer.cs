using System;
using System.Drawing;
using System.Windows.Forms;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;

namespace DBClusterVisualization {
    public partial class ClusterVisualizer : Form {
        private string connectionString;
        private List<Button> nodeButtons = new List<Button>();
        private System.Windows.Forms.Timer updateTimer;
        private Dictionary<int, Dictionary<string, long>> nodeCollectionStates = new Dictionary<int, Dictionary<string, long>>();
        private Label infoLabel;
        private Panel infoPanel;

        public ClusterVisualizer() {
            InitializeComponent();
            MinimumSize = new Size(500, 400);
            Resize += ClusterVisualizer_Resize;
            BackColor = Color.FromArgb(45, 45, 48);
            ForeColor = Color.White;
            ControlBox = true;

            connectionString = GenerateConnectionString();
            InitializeNodesFromDatabase();


            InitializeInfoPanel();


            updateTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private async void UpdateTimer_Tick(object sender, EventArgs e) {
            for (int i = 0; i < nodeButtons.Count; i++) {
                await CheckNodeForUpdates(i);
            }

            await UpdateInfoPanel();
        }

        private async Task CheckNodeForUpdates(int nodeIndex) {
            try {
                var client = new MongoClient(connectionString);
                var dbList = await client.ListDatabaseNamesAsync();
                var dbNames = await dbList.ToListAsync();

                var collectionStates = new Dictionary<string, long>();

                foreach (var dbName in dbNames) {
                    var db = client.GetDatabase(dbName);
                    var collections = await db.ListCollectionNamesAsync();
                    var collectionNames = await collections.ToListAsync();

                    foreach (var collectionName in collectionNames) {
                        if (collectionName.StartsWith("system.") || collectionName == "oplog.rs") {
                            continue;
                        }

                        var collection = db.GetCollection<BsonDocument>(collectionName);
                        var count = await collection.CountDocumentsAsync(new BsonDocument());
                        collectionStates[collectionName] = count;
                    }
                }

                if (!nodeCollectionStates.ContainsKey(nodeIndex)) {
                    nodeCollectionStates[nodeIndex] = collectionStates;
                }
                else {
                    var previousState = nodeCollectionStates[nodeIndex];
                    var addedDifferences = new Dictionary<string, (long previous, long current)>();
                    var removedDifferences = new Dictionary<string, (long previous, long current)>();

                    // Compare the previous and current states
                    foreach (var collection in collectionStates) {
                        long previousCount = previousState.ContainsKey(collection.Key) ? previousState[collection.Key] : 0;
                        if (collection.Value > previousCount) {
                            // Positive difference (Added)
                            addedDifferences[collection.Key] = (previousCount, collection.Value);
                        }
                        else if (collection.Value < previousCount) {
                            // Negative difference (Removed)
                            removedDifferences[collection.Key] = (previousCount, collection.Value);
                        }
                    }

                    foreach (var collection in previousState.Keys) {
                        if (!collectionStates.ContainsKey(collection)) {
                            // Collection removed entirely
                            removedDifferences[collection] = (previousState[collection], 0);
                        }
                    }

                    // React to changes
                    if (addedDifferences.Count > 0) {
                        nodeCollectionStates[nodeIndex] = collectionStates;

                        System.Diagnostics.Debug.WriteLine($"[Node {nodeIndex + 1}] Positive Changes Detected (Added):");

                        foreach (var diff in addedDifferences) {
                            long changeCount = diff.Value.current - diff.Value.previous;
                            System.Diagnostics.Debug.WriteLine(
                                $"Collection '{diff.Key}': Added {changeCount} documents. " +
                                $"(Previous: {diff.Value.previous}, Current: {diff.Value.current})"
                            );
                        }

                        // Blink node for added changes
                        BlinkNodeAdded(nodeIndex);
                        await Task.Delay(500);
                    }

                    if (removedDifferences.Count > 0) {
                        nodeCollectionStates[nodeIndex] = collectionStates;

                        System.Diagnostics.Debug.WriteLine($"[Node {nodeIndex + 1}] Negative Changes Detected (Removed):");

                        foreach (var diff in removedDifferences) {
                            long changeCount = diff.Value.previous - diff.Value.current;
                            System.Diagnostics.Debug.WriteLine(
                                $"Collection '{diff.Key}': Removed {changeCount} documents. " +
                                $"(Previous: {diff.Value.previous}, Current: {diff.Value.current})"
                            );
                        }

                        BlinkNodeRemove(nodeIndex);
                        await Task.Delay(500);
                    }
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error checking node {nodeIndex}: {ex.Message}");
            }
        }

        private async void BlinkNodeAdded(int nodeIndex) {
            var nodeButton = nodeButtons[nodeIndex];
            for (int i = 0; i < 6; i++) {
                nodeButton.FlatAppearance.BorderColor = i % 2 == 0 ? Color.Green : Color.White;
                await Task.Delay(300);
            }
            nodeButton.FlatAppearance.BorderColor = Color.White;
        }

        private async void BlinkNodeRemove(int nodeIndex) {
            var nodeButton = nodeButtons[nodeIndex];
            for (int i = 0; i < 6; i++) {
                nodeButton.FlatAppearance.BorderColor = i % 2 == 0 ? Color.Red : Color.White;
                await Task.Delay(300);
            }
            nodeButton.FlatAppearance.BorderColor = Color.White;
        }

        private string GenerateConnectionString() {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Title = "Select Docker Compose File",
                Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK) {
                try {
                    string fileContent = File.ReadAllText(openFileDialog.FileName);
                    MatchCollection matches = Regex.Matches(fileContent, @"ports:\s*-\s*(\d+):\d+");

                    List<string> hosts = new List<string>();
                    foreach (Match match in matches) {
                        hosts.Add($"127.0.0.1:{match.Groups[1].Value}");
                    }

                    if (hosts.Count > 0) {
                        return $"mongodb://{string.Join(",", hosts)}/?replicaSet=rs0";
                    }
                }
                catch (Exception ex) {
                    MessageBox.Show($"Error reading file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return string.Empty;
        }

        private async void InitializeNodesFromDatabase() {
            if (string.IsNullOrEmpty(connectionString)) {
                MessageBox.Show("Connection string could not be generated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try {
                var client = new MongoClient(connectionString);
                var adminDb = client.GetDatabase("admin");
                var command = new BsonDocument { { "replSetGetStatus", 1 } };
                var result = await adminDb.RunCommandAsync<BsonDocument>(command);

                var members = result["members"].AsBsonArray;
                int nodeCount = members.Count;
                InitializeNodes(nodeCount);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to retrieve node information: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeNodes(int nodeCount) {
            Random rand = new Random();
            nodeButtons.Clear();

            for (int i = 0; i < nodeCount; i++) {
                Button nodeButton = new Button {
                    Text = $"Node {i + 1}",
                    Name = $"Node{i + 1}",
                    Size = new Size(150, 75),
                    BackColor = Color.FromArgb(rand.Next(180, 255), rand.Next(180, 255), rand.Next(180, 255)),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.Black
                };
                nodeButton.FlatAppearance.BorderSize = 2;
                nodeButton.FlatAppearance.BorderColor = Color.White;
                nodeButton.Click += NodeButton_Click;
                Controls.Add(nodeButton);
                nodeButtons.Add(nodeButton);
            }
            ArrangeNodes();
        }

        private void ArrangeNodes() {
            int centerX = ClientSize.Width / 2;
            int centerY = ClientSize.Height / 2;
            int radius = Math.Min(ClientSize.Width, ClientSize.Height) / 3;
            double angleStep = 2 * Math.PI / nodeButtons.Count;

            for (int i = 0; i < nodeButtons.Count; i++) {
                double angle = i * angleStep;
                int x = centerX + (int)(radius * Math.Cos(angle)) - nodeButtons[i].Width / 2;
                int y = centerY + (int)(radius * Math.Sin(angle)) - nodeButtons[i].Height / 2;
                nodeButtons[i].Location = new Point(x, y);
            }
            Invalidate();
        }

        private void ClusterVisualizer_Resize(object sender, EventArgs e) {
            ArrangeNodes();
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Pen pen = new Pen(Color.White, 2);
            for (int i = 0; i < nodeButtons.Count; i++) {
                for (int j = i + 1; j < nodeButtons.Count; j++) {
                    e.Graphics.DrawLine(pen, GetCenter(nodeButtons[i]), GetCenter(nodeButtons[j]));
                }
            }
        }

        private Point GetCenter(Button button) {
            return new Point(button.Location.X + button.Width / 2, button.Location.Y + button.Height / 2);
        }

        private async void NodeButton_Click(object sender, EventArgs e) {
            Button clickedButton = sender as Button;
            int nodeIndex = int.Parse(clickedButton.Name.Replace("Node", "")) - 1;

            // Prompt user for the database name
            string databaseName = PromptForDatabaseName();

            if (string.IsNullOrEmpty(databaseName)) {
                MessageBox.Show("Database name cannot be empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try {
                var client = new MongoClient(connectionString);
                var db = client.GetDatabase(databaseName);

                var collections = await db.ListCollectionNamesAsync();
                var collectionNames = await collections.ToListAsync();

                if (collectionNames.Count == 0) {
                    MessageBox.Show("No collections found in the database.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Open a new form to display collections and documents
                using (var dbViewer = new DatabaseViewer(db, collectionNames)) {
                    dbViewer.ShowDialog();
                }
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to load collections: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private string PromptForDatabaseName() {
            using (var prompt = new Form()) {
                // Set up form appearance
                prompt.Text = "Enter Database Name";
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.Size = new Size(400, 150);
                prompt.BackColor = Color.FromArgb(30, 30, 30); 


                var textBox = new TextBox {
                    Dock = DockStyle.Top,
                    Margin = new Padding(20, 5, 20, 10),
                    Font = new Font("Arial", 10),
                    PlaceholderText = "e.g. testDB",
                    BackColor = Color.FromArgb(50, 50, 50), 
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var button = new Button {
                    Text = "OK",
                    Dock = DockStyle.Bottom,
                    Height = 40,
                    BackColor = Color.FromArgb(0, 122, 204), 
                    ForeColor = Color.White,
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += (sender, e) => prompt.Close();

                // Suppress system sound by handling the button click
                button.Click += (sender, e) =>
                {
                    prompt.DialogResult = DialogResult.OK;
                };



                prompt.Controls.Add(textBox);
                prompt.Controls.Add(button);

                prompt.ShowDialog();

                return textBox.Text.Trim();
            }
        }

        private void InitializeInfoPanel() {
            infoPanel = new Panel {
                Width = 300,
                BackColor = Color.FromArgb(60, 60, 60),
                Location = new Point(ClientSize.Width - 310, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Visible = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            infoLabel = new Label {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Arial", 10),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(5),
                Text = "Loading information..."
            };

            infoPanel.Controls.Add(infoLabel);
            Controls.Add(infoPanel);




            AdjustInfoPanelHeight(infoPanel);

            infoPanel.BringToFront();
        }

        private async Task UpdateInfoPanel() {
            int totalDocuments = 0;
            double storageSize = 0;

            try {
                var client = new MongoClient(connectionString);
                var dbList = await client.ListDatabaseNamesAsync();
                var dbNames = await dbList.ToListAsync();

                foreach (var dbName in dbNames) {
                    var db = client.GetDatabase(dbName);
                    var collections = await db.ListCollectionNamesAsync();
                    var collectionNames = await collections.ToListAsync();

                    foreach (var collectionName in collectionNames) {
                        if (collectionName.StartsWith("system.") || collectionName == "oplog.rs") {
                            continue;
                        }

                        var collection = db.GetCollection<BsonDocument>(collectionName);
                        var count = await collection.CountDocumentsAsync(new BsonDocument());
                        totalDocuments += (int)count;
                    }
                }

                var stats = await client.GetDatabase(dbNames[0]).RunCommandAsync<BsonDocument>(new BsonDocument { { "dbStats", 1 } });
                storageSize += stats["storageSize"].ToDouble() / (1024 * 1024);


                int databaseCount = dbNames.Count;


                infoLabel.Text = $"Cluster Information:\n" +
                                 $"- Nodes: {nodeButtons.Count}\n" +
                                 $"- Total Documents: {totalDocuments}\n" +
                                 $"- Storage Size: {storageSize:F2} MB\n" +
                                 $"- Databases: {databaseCount}\n" +
                                 $"- Last Updated: {DateTime.Now}";
                infoLabel.TextAlign = ContentAlignment.MiddleLeft;




                AdjustInfoPanelHeight(infoLabel.Parent as Panel);
            }
            catch (Exception ex) {
                infoLabel.Text = "Failed to load cluster information.";
                System.Diagnostics.Debug.WriteLine($"Error updating info panel: {ex.Message}");
            }
        }

        private void AdjustInfoPanelHeight(Panel infoPanel) {
            if (infoLabel != null) {

                var lines = infoLabel.Text.Split(new[] { '\n' }, StringSplitOptions.None);


                int lineHeight = TextRenderer.MeasureText("Sample", infoLabel.Font).Height;
                int requiredHeight = (lines.Length * lineHeight) + 15;


                infoPanel.Height = requiredHeight;
            }
        }
    }

    public class DatabaseViewer : Form {
        private readonly IMongoDatabase database;
        private readonly List<string> collectionNames;

        private ListBox collectionListBox;
        private DataGridView documentGridView;

        public DatabaseViewer(IMongoDatabase database, List<string> collectionNames) {
            this.database = database;
            this.collectionNames = collectionNames;

            InitializeForm();
        }

        private void InitializeForm() {
            Text = "Database Viewer";
            Size = new Size(800, 600);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            // Collection ListBox
            collectionListBox = new ListBox {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
            };
            collectionListBox.SelectedIndexChanged += CollectionListBox_SelectedIndexChanged;

            // Populate collection names
            foreach (var name in collectionNames) {
                collectionListBox.Items.Add(name);
            }

            // DataGridView for documents
            documentGridView = new DataGridView {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                GridColor = Color.Gray,
                RowHeadersVisible = false,
                ReadOnly = true, // Make the table read-only
                AllowUserToAddRows = false, // Prevent adding new rows
                AllowUserToDeleteRows = false, // Prevent deleting rows
                AllowUserToResizeColumns = false, // Lock column resizing
                AllowUserToResizeRows = false, // Lock row resizing
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, // Automatically adjust column width
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, // Automatically adjust row height
                EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                    BackColor = Color.FromArgb(60, 60, 60),
                    ForeColor = Color.White,
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                },
                DefaultCellStyle = new DataGridViewCellStyle {
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.White,
                    SelectionBackColor = Color.FromArgb(70, 70, 70),
                    SelectionForeColor = Color.White
                },
            };

            // Add controls to form
            Controls.Add(documentGridView);
            Controls.Add(collectionListBox);
        }

        private async void CollectionListBox_SelectedIndexChanged(object sender, EventArgs e) {
            string selectedCollection = collectionListBox.SelectedItem.ToString();

            try {
                var collection = database.GetCollection<BsonDocument>(selectedCollection);
                var documents = await collection.Find(new BsonDocument()).ToListAsync();

                // Convert documents to a DataTable
                var dataTable = new DataTable();
                foreach (var doc in documents) {
                    foreach (var element in doc.Elements) {
                        if (!dataTable.Columns.Contains(element.Name))
                            dataTable.Columns.Add(element.Name);
                    }
                }

                foreach (var doc in documents) {
                    var row = dataTable.NewRow();
                    foreach (var element in doc.Elements) {
                        row[element.Name] = element.Value.ToString();
                    }
                    dataTable.Rows.Add(row);
                }

                // Bind DataTable to DataGridView
                documentGridView.DataSource = dataTable;

                // Adjust the size of the DataGridView to fit the content
                AdjustDataGridViewSize();
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to load documents: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AdjustDataGridViewSize() {
            // Set the width of the DataGridView to the sum of all column widths
            int totalWidth = 0;
            foreach (DataGridViewColumn column in documentGridView.Columns) {
                totalWidth += column.Width;
            }

            // Set the height of the DataGridView to the sum of all row heights
            int totalHeight = documentGridView.ColumnHeadersHeight;
            foreach (DataGridViewRow row in documentGridView.Rows) {
                totalHeight += row.Height;
            }

            // Set the new size for the DataGridView
            documentGridView.Width = Math.Min(totalWidth, ClientSize.Width - collectionListBox.Width - 20); // Fit within the form
            documentGridView.Height = Math.Min(totalHeight, ClientSize.Height - 20); // Fit within the form
        }
    }
}