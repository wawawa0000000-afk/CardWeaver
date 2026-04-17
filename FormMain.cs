using CardWeaver.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;
using CardWeaver.Models;
using System.IO;
using System.Text.Json;

namespace CardWeaver
{
    public partial class FormMain : Form
    {
        private float zoomFactor = 1.0f;
        private List<(CardControl from, CardControl to)> connections = new();
        private bool isConnecting = false;
        private CardControl? pendingFrom = null;
        private Button? btnStartConnect;
        private string? currentFilePath = null;
        private List<BoxControl> boxes = new();
        private LineOverlayForm? lineOverlay;
        private Dictionary<Control, BoxControl> parentMap = new();

        private bool isPanning = false;
        private Point lastMousePos;
        private Point gridOffset = Point.Empty;
        private const int BASE_GRID_SIZE = 40;
        private bool isDarkMode = false;
        private ToolStripLabel? lblCurrentFile;
        private bool suppressOverlayUpdate = false;
        private CardData? clipboardCardData = null;
        private CardControl? lastActiveCard = null;

        public FormMain()
        {
            InitializeComponent();
            InitializeZoomControls();

            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.UpdateStyles();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.ControlRemoved += FormMain_ControlRemoved;
            lineOverlay = new LineOverlayForm(this);
            lineOverlay.Show(this);
            UpdateOverlayBounds();
        }

        private void FormMain_ControlRemoved(object? sender, ControlEventArgs e)
        {
            if (e.Control is BoxControl box)
            {
                boxes.Remove(box);
            }
            if (e.Control is CardControl card)
            {
                connections.RemoveAll(c => c.from == card || c.to == card);
            }
            RebuildHierarchy();
            InvalidateOverlay();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (lineOverlay != null && !lineOverlay.IsDisposed)
            {
                lineOverlay.Dispose();
            }
        }

        private void UpdateOverlayBounds()
        {
            if (lineOverlay != null && !lineOverlay.IsDisposed && this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                lineOverlay.Bounds = this.RectangleToScreen(this.ClientRectangle);
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            UpdateOverlayBounds();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateOverlayBounds();
            InvalidateOverlay();
        }

        public void InvalidateOverlay(bool forceUpdate = false)
        {
            if (suppressOverlayUpdate) return;
            if (lineOverlay != null && !lineOverlay.IsDisposed)
            {
                lineOverlay.Invalidate();
                if (forceUpdate)
                {
                    lineOverlay.Update();
                }
            }
        }

        private void RebuildHierarchy()
        {
            parentMap.Clear();
            var allCards = this.Controls.OfType<CardControl>().ToList();
            var allBoxes = this.Controls.OfType<BoxControl>().ToList();

            var sortedBoxes = allBoxes.OrderByDescending(b => b.Width * b.Height).ToList();

            foreach (var box in sortedBoxes)
            {
                Point center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
                var parent = sortedBoxes.Where(p => p != box && p.Bounds.Contains(center) && (p.Width * p.Height > box.Width * box.Height))
                                        .OrderBy(p => p.Width * p.Height)
                                        .FirstOrDefault();
                if (parent != null)
                {
                    parentMap[box] = parent;
                }
            }

            foreach (var card in allCards)
            {
                Point center = new Point(card.Left + card.Width / 2, card.Top + card.Height / 2);
                var parent = allBoxes.Where(p => p.Bounds.Contains(center))
                                     .OrderBy(p => p.Width * p.Height)
                                     .FirstOrDefault();
                if (parent != null)
                {
                    parentMap[card] = parent;
                }
            }
        }

        private void Box_Dragged(BoxControl sender, int dx, int dy)
        {
            suppressOverlayUpdate = true;
            MoveChildren(sender, dx, dy);
            suppressOverlayUpdate = false;
            InvalidateOverlay(true);
        }

        private void MoveChildren(BoxControl parentBox, int dx, int dy)
        {
            var childrenToMove = parentMap.Where(kvp => kvp.Value == parentBox).Select(kvp => kvp.Key).ToList();

            foreach (var child in childrenToMove)
            {
                child.Location = new Point(child.Left + dx, child.Top + dy);

                if (child is BoxControl childBox)
                {
                    MoveChildren(childBox, dx, dy);
                }
            }
        }

        private void InitializeZoomControls()
        {
            var menuStrip = new MenuStrip();

            var fileMenu = new ToolStripMenuItem("ファイル");

            var newItem = new ToolStripMenuItem("新規作成");
            newItem.Click += (s, e) =>
            {
                if (MessageBox.Show("現在のキャンバスをクリアしますか？未保存のデータは失われます。", "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    this.ControlRemoved -= FormMain_ControlRemoved;
                    foreach (var card in this.Controls.OfType<CardControl>().ToList()) this.Controls.Remove(card);
                    foreach (var box in boxes.ToList()) this.Controls.Remove(box);
                    boxes.Clear();
                    connections.Clear();
                    this.ControlRemoved += FormMain_ControlRemoved;
                    
                    currentFilePath = null;
                    UpdateTitleDisplay();
                    RebuildHierarchy();
                    UpdateZOrder();
                    InvalidateOverlay(true);
                }
            };

            var saveItem = new ToolStripMenuItem("名前を付けて保存");
            saveItem.Click += (s, e) =>
            {
                using (var dialog = new SaveFileDialog())
                {
                    if (!string.IsNullOrEmpty(currentFilePath))
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(currentFilePath);
                    }
                    else
                    {
                        dialog.InitialDirectory = GetLastDirectory();
                    }
                    dialog.Filter = "JSONファイル (*.json)|*.json";
                    dialog.Title = "ワークスペースを保存";

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string? dir = Path.GetDirectoryName(dialog.FileName);
                        if (dir != null) SaveLastDirectory(dir);
                        SaveWorkspace(dialog.FileName);
                        currentFilePath = dialog.FileName;
                    }
                }
            };

            var overwriteItem = new ToolStripMenuItem("上書き保存");
            overwriteItem.ShortcutKeys = Keys.Control | Keys.S;
            overwriteItem.ShowShortcutKeys = true;
            overwriteItem.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    SaveWorkspace(currentFilePath);
                }
                else
                {
                    MessageBox.Show("まだ保存ファイルが選択されていません。先に保存または読み込みしてください。");
                }
            };

            var loadItem = new ToolStripMenuItem("開く");
            loadItem.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog())
                {
                    if (!string.IsNullOrEmpty(currentFilePath))
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(currentFilePath);
                    }
                    else
                    {
                        dialog.InitialDirectory = GetLastDirectory();
                    }
                    dialog.Filter = "JSONファイル (*.json)|*.json";
                    dialog.Title = "ワークスペースを読み込み";

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string? dir = Path.GetDirectoryName(dialog.FileName);
                        if (dir != null) SaveLastDirectory(dir);
                        LoadWorkspace(dialog.FileName);
                        currentFilePath = dialog.FileName;
                    }
                }
            };

            fileMenu.DropDownItems.Add(newItem);
            fileMenu.DropDownItems.Add(saveItem);
            fileMenu.DropDownItems.Add(overwriteItem);
            fileMenu.DropDownItems.Add(loadItem);

            var viewMenu = new ToolStripMenuItem("表示");
            var lightModeItem = new ToolStripMenuItem("ライトモード");
            var darkModeItem = new ToolStripMenuItem("ダークモード");

            lightModeItem.Checked = true;

            lightModeItem.Click += (s, e) =>
            {
                isDarkMode = false;
                lightModeItem.Checked = true;
                darkModeItem.Checked = false;
                this.BackColor = SystemColors.Control;
                this.Invalidate();
            };

            darkModeItem.Click += (s, e) =>
            {
                isDarkMode = true;
                lightModeItem.Checked = false;
                darkModeItem.Checked = true;
                this.BackColor = Color.FromArgb(20, 30, 50); // 濃い紺色
                this.Invalidate();
            };

            viewMenu.DropDownItems.Add(lightModeItem);
            viewMenu.DropDownItems.Add(darkModeItem);

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(viewMenu);

            lblCurrentFile = new ToolStripLabel
            {
                Alignment = ToolStripItemAlignment.Right,
                Text = "新規プロジェクト",
                ForeColor = Color.DarkGray,
                Font = new Font("Meiryo", 9, FontStyle.Bold)
            };
            menuStrip.Items.Add(lblCurrentFile);

            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            int topOffset = menuStrip.Height + 10;

            var btnZoomIn = new Button
            {
                Text = "拡大",
                Location = new Point(10, topOffset),
                Size = new Size(60, 30),
                BackColor = Color.White,
                ForeColor = Color.Black
            };
            btnZoomIn.Click += (s, e) => ChangeZoom(1.1f);
            this.Controls.Add(btnZoomIn);

            var btnZoomOut = new Button
            {
                Text = "縮小",
                Location = new Point(80, topOffset),
                Size = new Size(60, 30),
                BackColor = Color.White,
                ForeColor = Color.Black
            };
            btnZoomOut.Click += (s, e) => ChangeZoom(0.9f);
            this.Controls.Add(btnZoomOut);

            var btnAddCard = new Button
            {
                Text = "カード追加",
                Location = new Point(150, topOffset),
                Size = new Size(80, 30),
                BackColor = Color.White,
                ForeColor = Color.Black
            };
            btnAddCard.Click += btnAddCard_Click;
            this.Controls.Add(btnAddCard);

            var btnAddBox = new Button
            {
                Text = "ボックス追加",
                Location = new Point(240, topOffset),
                Size = new Size(100, 30),
                BackColor = Color.White,
                ForeColor = Color.Black
            };
            btnAddBox.Click += btnAddBox_Click;
            this.Controls.Add(btnAddBox);

            btnStartConnect = new Button
            {
                Text = "接続モード",
                Location = new Point(350, topOffset),
                Size = new Size(100, 30),
                BackColor = Color.White,
                ForeColor = Color.Black
            };
            btnStartConnect.Click += (s, e) =>
            {
                isConnecting = true;
                pendingFrom = null;
                if (btnStartConnect != null)
                    btnStartConnect.BackColor = Color.LightGreen;
            };
            this.Controls.Add(btnStartConnect);
        }

        private void btnAddCard_Click(object? sender, EventArgs e)
        {
            var card = new CardWeaver.Controls.CardControl();
            card.Location = new Point(50 + this.Controls.Count * 10, 60 + this.Controls.Count * 10);
            ApplyZoomToCard(card);
            this.Controls.Add(card);
            card.CardClicked += Card_Clicked;
            card.LocationChanged += (s, e) => InvalidateOverlay(true);
            card.SizeChanged += (s, e) => InvalidateOverlay(true);
            card.Dropped += (s, e) => RebuildHierarchy();
            card.ComponentActivated += (s, e) => UpdateZOrder(card);
            RebuildHierarchy();
            UpdateZOrder(card);
        }

        private void btnAddBox_Click(object? sender, EventArgs e)
        {
            var box = new BoxControl();
            box.Location = new Point(50 + boxes.Count * 20, 200 + boxes.Count * 20);
            ApplyZoomToBox(box);
            this.Controls.Add(box);
            boxes.Add(box);
            box.Dropped += (s, e) => RebuildHierarchy();
            box.Dragged += Box_Dragged;
            box.ComponentActivated += (s, e) => UpdateZOrder(box);
            RebuildHierarchy();
            UpdateZOrder(box);
        }

        private void ChangeZoom(float factor)
        {
            float newZoom = zoomFactor * factor;

            if (newZoom < 0.25f) newZoom = 0.25f;
            if (newZoom > 3.0f) newZoom = 3.0f;

            if (newZoom == zoomFactor) return;

            zoomFactor = newZoom;

            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is CardWeaver.Controls.CardControl card)
                {
                    ApplyZoomToCard(card);
                }
                else if (ctrl is BoxControl box)
                {
                    ApplyZoomToBox(box);
                }
            }
            this.Invalidate();
        }

        private void ApplyZoomToCard(CardWeaver.Controls.CardControl card)
        {
            card.CurrentZoom = zoomFactor;
            card.Size = new Size(
                (int)(card.BaseSize.Width * zoomFactor),
                (int)(card.BaseSize.Height * zoomFactor)
            );
        }

        private void ApplyZoomToBox(BoxControl box)
        {
            box.CurrentZoom = zoomFactor;
            box.Size = new Size(
                (int)(box.BaseSize.Width * zoomFactor),
                (int)(box.BaseSize.Height * zoomFactor)
            );
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                var clicked = connections.FirstOrDefault(conn =>
                {
                    Point p1 = new Point(conn.from.Left + conn.from.Width / 2, conn.from.Top + 6);
                    Point p2 = new Point(conn.to.Left + conn.to.Width / 2, conn.to.Top + 6);
                    var dist = DistanceToSegment(e.Location, p1, p2);
                    return dist < 6;
                });

                if (clicked != default)
                {
                    connections.Remove(clicked);
                    InvalidateOverlay(true);
                }
            }
            else if (e.Button == MouseButtons.Left && !isConnecting)
            {
                isPanning = true;
                lastMousePos = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (isPanning)
            {
                int dx = e.X - lastMousePos.X;
                int dy = e.Y - lastMousePos.Y;
                
                if (dx != 0 || dy != 0)
                {
                    gridOffset.X += dx;
                    gridOffset.Y += dy;

                    suppressOverlayUpdate = true;
                    this.SuspendLayout();
                    foreach (Control ctrl in this.Controls.OfType<Control>().ToList())
                    {
                        if (ctrl is CardControl || ctrl is BoxControl)
                        {
                            ctrl.Location = new Point(ctrl.Left + dx, ctrl.Top + dy);
                        }
                    }
                    this.ResumeLayout();
                    suppressOverlayUpdate = false;
                    
                    lastMousePos = e.Location;
                    InvalidateOverlay(true);
                    this.Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                isPanning = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (lastActiveCard != null)
                {
                    clipboardCardData = new CardData
                    {
                        Location = lastActiveCard.Location,
                        ColorName = lastActiveCard.BackColor.Name,
                        Text = lastActiveCard.CardText,
                        Title = lastActiveCard.TitleText,
                        Width = lastActiveCard.BaseSize.Width,
                        Height = lastActiveCard.BaseSize.Height
                    };
                }
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                if (clipboardCardData != null)
                {
                    var card = new CardControl();
                    card.Location = new Point(clipboardCardData.Location.X + 20, clipboardCardData.Location.Y + 20);
                    var loadedColor = Color.FromName(clipboardCardData.ColorName ?? "LightYellow");
                    card.BackColor = loadedColor.IsKnownColor ? loadedColor : Color.LightYellow;
                    card.CardText = clipboardCardData.Text ?? string.Empty;
                    card.TitleText = clipboardCardData.Title ?? "カードタイトル";
                    card.BaseSize = new Size(clipboardCardData.Width, clipboardCardData.Height);
                    ApplyZoomToCard(card);

                    card.CardClicked += Card_Clicked;
                    card.LocationChanged += (s, ev) => InvalidateOverlay(true);
                    card.SizeChanged += (s, ev) => InvalidateOverlay(true);
                    card.Dropped += (s, ev) => RebuildHierarchy();
                    card.ComponentActivated += (s, ev) => UpdateZOrder(card);

                    this.Controls.Add(card);
                    RebuildHierarchy();
                    UpdateZOrder(card);

                    clipboardCardData.Location = card.Location;
                }
            }
        }

        private void ConnectCards(CardControl from, CardControl to)
        {
            connections.Add((from, to));
            InvalidateOverlay(true);
        }

        private void Card_Clicked(object? sender, EventArgs e)
        {
            if (!isConnecting || sender is not CardControl clickedCard)
                return;

            if (pendingFrom == null)
            {
                pendingFrom = clickedCard;
                clickedCard.BackColor = Color.Orange;
            }
            else
            {
                ConnectCards(pendingFrom, clickedCard);
                pendingFrom.BackColor = Color.LightYellow;
                pendingFrom = null;
                isConnecting = false;

                if (btnStartConnect != null)
                    btnStartConnect.BackColor = Color.White;
            }

            InvalidateOverlay(true);
        }

        private float DistanceToSegment(Point p, Point a, Point b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            if (dx == 0 && dy == 0) return Distance(p, a);

            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            float projX = a.X + t * dx;
            float projY = a.Y + t * dy;
            return Distance(p, new Point((int)projX, (int)projY));
        }

        private float Distance(Point p1, Point p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 賽の目（グリッド）の描画
            int gridSize = (int)(BASE_GRID_SIZE * zoomFactor);
            if (gridSize < 10) gridSize = 10;
            
            Color gridColor = isDarkMode ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(220, 220, 220);
            using (Pen gridPen = new Pen(gridColor))
            {
                int startX = gridOffset.X % gridSize;
                if (startX > 0) startX -= gridSize;

                for (int x = startX; x < this.ClientSize.Width; x += gridSize)
                {
                    e.Graphics.DrawLine(gridPen, x, 0, x, this.ClientSize.Height);
                }

                int startY = gridOffset.Y % gridSize;
                if (startY > 0) startY -= gridSize;

                for (int y = startY; y < this.ClientSize.Height; y += gridSize)
                {
                    e.Graphics.DrawLine(gridPen, 0, y, this.ClientSize.Width, y);
                }
            }
        }

        public void DrawConnectionsOnGraphics(Graphics g)
        {
            using Pen pen = new Pen(Color.Black, 2);
            foreach (var (from, to) in connections)
            {
                Point p1 = new Point(from.Left + from.Width / 2, from.Top + 6);
                Point p2 = new Point(to.Left + to.Width / 2, to.Top + 6);
                g.DrawLine(pen, p1, p2);
            }
        }

        private void SaveWorkspace(string path)
        {
            var data = new WorkspaceData();
            var cards = this.Controls.OfType<CardControl>().ToList();

            foreach (var card in cards)
            {
                data.Cards.Add(new CardData
                {
                    Location = card.Location,
                    ColorName = card.BackColor.Name,
                    Text = card.CardText,
                    Title = card.TitleText,
                    Width = card.BaseSize.Width,
                    Height = card.BaseSize.Height
                });
            }

            foreach (var box in boxes)
            {
                data.Boxes.Add(new BoxData
                {
                    Location = box.Location,
                    ColorName = box.BackColor.Name,
                    Text = box.BoxText,
                    Width = box.BaseSize.Width,
                    Height = box.BaseSize.Height
                });
            }

            foreach (var conn in connections)
            {
                int fromIndex = cards.IndexOf(conn.from);
                int toIndex = cards.IndexOf(conn.to);
                if (fromIndex >= 0 && toIndex >= 0)
                {
                    data.Connections.Add(new ConnectionData
                    {
                        FromIndex = fromIndex,
                        ToIndex = toIndex
                    });
                }
            }

            string json = JsonSerializer.Serialize(data);
            File.WriteAllText(path, json);
            currentFilePath = path;
            UpdateTitleDisplay();
        }

        private void UpdateTitleDisplay()
        {
            if (lblCurrentFile != null)
            {
                lblCurrentFile.Text = string.IsNullOrEmpty(currentFilePath) ? "新規プロジェクト" : Path.GetFileName(currentFilePath);
            }
        }

        private void LoadWorkspace(string path)
        {
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<WorkspaceData>(json);

            if (data == null) return;

            this.ControlRemoved -= FormMain_ControlRemoved;

            foreach (var card in this.Controls.OfType<CardControl>().ToList())
            {
                this.Controls.Remove(card);
            }
            foreach (var box in boxes.ToList())
            {
                this.Controls.Remove(box);
            }
            boxes.Clear();
            connections.Clear();

            this.ControlRemoved += FormMain_ControlRemoved;

            var cards = new List<CardControl>();
            foreach (var cardData in data.Cards)
            {
                var card = new CardControl();
                card.Location = cardData.Location;
                var loadedColor = Color.FromName(cardData.ColorName ?? "LightYellow");
                card.BackColor = loadedColor.IsKnownColor ? loadedColor : Color.LightYellow;
                card.CardText = cardData.Text ?? string.Empty;
                card.TitleText = cardData.Title ?? "カードタイトル";
                if (cardData.Width > 0 && cardData.Height > 0)
                {
                    card.BaseSize = new Size(cardData.Width, cardData.Height);
                }
                ApplyZoomToCard(card);
                card.CardClicked += Card_Clicked;
                card.LocationChanged += (s, e) => InvalidateOverlay(true);
                card.SizeChanged += (s, e) => InvalidateOverlay(true);
                card.Dropped += (s, e) => RebuildHierarchy();
                card.ComponentActivated += (s, e) => UpdateZOrder(card);
                this.Controls.Add(card);
                cards.Add(card);
            }

            foreach (var boxData in data.Boxes)
            {
                var box = new BoxControl();
                box.Location = boxData.Location;
                var loadedColor = Color.FromName(boxData.ColorName ?? "LightBlue");
                box.BackColor = loadedColor.IsKnownColor ? loadedColor : Color.LightBlue;
                box.BoxText = boxData.Text ?? string.Empty;
                if (boxData.Width > 0 && boxData.Height > 0)
                {
                    box.BaseSize = new Size(boxData.Width, boxData.Height);
                }
                ApplyZoomToBox(box);
                this.Controls.Add(box);
                boxes.Add(box);
                box.Dropped += (s, e) => RebuildHierarchy();
                box.Dragged += Box_Dragged;
                box.ComponentActivated += (s, e) => UpdateZOrder(box);
            }

            foreach (var conn in data.Connections)
            {
                if (conn.FromIndex < cards.Count && conn.ToIndex < cards.Count)
                {
                    ConnectCards(cards[conn.FromIndex], cards[conn.ToIndex]);
                }
            }

            currentFilePath = path;
            UpdateTitleDisplay();
            RebuildHierarchy();
            UpdateZOrder();
            InvalidateOverlay(true);
        }

        private string GetSettingsFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardWeaverSettings.txt");
        }

        private void SaveLastDirectory(string dir)
        {
            try { File.WriteAllText(GetSettingsFilePath(), dir); } catch { }
        }

        private string GetLastDirectory()
        {
            try 
            {
                if (File.Exists(GetSettingsFilePath())) 
                    return File.ReadAllText(GetSettingsFilePath());
            } 
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public void UpdateZOrder(Control? activeControl = null)
        {
            if (activeControl is CardControl actCard)
            {
                lastActiveCard = actCard;
            }

            this.SuspendLayout();

            var allCards = this.Controls.OfType<CardControl>().ToList();
            var allBoxes = this.Controls.OfType<BoxControl>().ToList();

            foreach (var box in allBoxes)
            {
                if (box != activeControl)
                {
                    box.BringToFront();
                }
            }
            if (activeControl is BoxControl activeBox)
            {
                activeBox.BringToFront();
            }

            foreach (var card in allCards)
            {
                if (card != activeControl)
                {
                    card.BringToFront();
                }
            }
            if (activeControl is CardControl activeCard)
            {
                activeCard.BringToFront();
            }

            var uiControls = this.Controls.OfType<Control>()
                .Where(c => !(c is CardControl) && !(c is BoxControl))
                .ToList();
            foreach (var ui in uiControls)
            {
                ui.BringToFront();
            }

            this.ResumeLayout();
            this.Invalidate();
        }
    }

    public class LineOverlayForm : Form
    {
        private FormMain _parent;

        public LineOverlayForm(FormMain parent)
        {
            _parent = parent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000;  // WS_EX_LAYERED
                cp.ExStyle |= 0x20;     // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            _parent.DrawConnectionsOnGraphics(e.Graphics);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }
    }
}