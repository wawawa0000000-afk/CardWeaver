using System;
using System.Drawing;
using System.Windows.Forms;

namespace CardWeaver.Controls
{
    public class BoxControl : UserControl
    {
        private Label headerLabel;
        private Label contentLabel;
        private TextBox editBox;
        
        private Point dragOffset;
        private bool isDragging = false;
        
        private enum ResizeDir { None, Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeDir currentResizeDir = ResizeDir.None;
        private bool isResizing = false;
        private Rectangle originalBounds;
        private Point resizeStart;

        private ContextMenuStrip? colorMenu;

        private static readonly Color[] colorOptions = new Color[]
        {
            Color.LightYellow, Color.LightBlue, Color.LightGreen, Color.LightPink,
            Color.LightGray, Color.LightCoral, Color.LightCyan, Color.LightGoldenrodYellow,
            Color.LightSalmon, Color.LightSeaGreen, Color.LightSkyBlue, Color.LightSteelBlue,
            Color.MistyRose, Color.PaleGreen, Color.PeachPuff, Color.Thistle
        };

        private Color baseBackColor = Color.LightBlue;

        public Size BaseSize { get; set; } = new Size(150, 80);
        public float CurrentZoom { get; set; } = 1.0f;

        public event EventHandler? Dropped;
        public event Action<BoxControl, int, int>? Dragged;

        public BoxControl()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.SetStyle(ControlStyles.Opaque, false);
            this.Size = new Size(150, 80);
            this.BackColor = baseBackColor;
            this.BorderStyle = BorderStyle.None;
            this.DoubleBuffered = true;

            headerLabel = new Label
            {
                Text = "ボックス",
                Dock = DockStyle.Top,
                Font = new Font("Meiryo", 9, FontStyle.Bold),
                Height = 20,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = Color.Black
            };

            contentLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Meiryo", 12),
                Text = "ボックス",
                BackColor = Color.Transparent
            };

            editBox = new TextBox
            {
                Visible = false,
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font("Meiryo", 12)
            };

            this.MouseDown += BoxControl_MouseDown;
            this.MouseMove += BoxControl_MouseMove;
            this.MouseUp += BoxControl_MouseUp;

            headerLabel.MouseDown += BoxControl_MouseDown;
            headerLabel.MouseMove += BoxControl_MouseMove;
            headerLabel.MouseUp += BoxControl_MouseUp;

            contentLabel.MouseDown += BoxControl_MouseDown;
            contentLabel.MouseMove += BoxControl_MouseMove;
            contentLabel.MouseUp += BoxControl_MouseUp;

            contentLabel.DoubleClick += (s, e) => StartEdit();
            editBox.Leave += (s, e) => EndEdit();
            editBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) EndEdit(false); };

            this.Controls.Add(editBox);
            this.Controls.Add(contentLabel);
            this.Controls.Add(headerLabel);

            InitializeColorMenu();
            this.ContextMenuStrip = colorMenu;
            contentLabel.ContextMenuStrip = colorMenu;
        }

        private void StartEdit()
        {
            editBox.Text = contentLabel.Text;
            contentLabel.Visible = false;
            editBox.Visible = true;
            editBox.BringToFront();
            editBox.Focus();
        }

        private void EndEdit(bool save = true)
        {
            if (save) contentLabel.Text = editBox.Text;
            editBox.Visible = false;
            contentLabel.Visible = true;
        }

        private void BoxControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (currentResizeDir != ResizeDir.None)
                {
                    isResizing = true;
                    originalBounds = this.Bounds;
                    if (sender is Control control)
                        resizeStart = control.PointToScreen(e.Location);
                }
                else
                {
                    isDragging = true;
                    if (sender is Control control)
                    {
                        Point screenPos = control.PointToScreen(e.Location);
                        dragOffset = this.PointToClient(screenPos);
                    }
                }
            }
        }

        private void BoxControl_MouseMove(object? sender, MouseEventArgs e)
        {
            if (sender is not Control control) return;
            Point screenPos = control.PointToScreen(e.Location);
            Point clientPos = this.PointToClient(screenPos);

            if (isResizing)
            {
                int dx = screenPos.X - resizeStart.X;
                int dy = screenPos.Y - resizeStart.Y;
                int newX = originalBounds.X, newY = originalBounds.Y;
                int newW = originalBounds.Width, newH = originalBounds.Height;

                if (currentResizeDir == ResizeDir.Right || currentResizeDir == ResizeDir.TopRight || currentResizeDir == ResizeDir.BottomRight)
                    newW += dx;
                if (currentResizeDir == ResizeDir.Bottom || currentResizeDir == ResizeDir.BottomLeft || currentResizeDir == ResizeDir.BottomRight)
                    newH += dy;
                if (currentResizeDir == ResizeDir.Left || currentResizeDir == ResizeDir.TopLeft || currentResizeDir == ResizeDir.BottomLeft)
                { newX += dx; newW -= dx; }
                if (currentResizeDir == ResizeDir.Top || currentResizeDir == ResizeDir.TopLeft || currentResizeDir == ResizeDir.TopRight)
                { newY += dy; newH -= dy; }

                newW = Math.Max(50, newW);
                newH = Math.Max(50, newH);

                var oldBounds = this.Bounds;
                this.Bounds = new Rectangle(newX, newY, newW, newH);
                this.BaseSize = new Size((int)(newW / CurrentZoom), (int)(newH / CurrentZoom));
                this.Parent?.Invalidate(oldBounds);
                this.Parent?.Update();
            }
            else if (isDragging)
            {
                Point parentPos = this.Parent?.PointToClient(screenPos) ?? Point.Empty;
                Point newPos = new Point(parentPos.X - dragOffset.X, parentPos.Y - dragOffset.Y);
                
                if (newPos != this.Location)
                {
                    int dx = newPos.X - this.Location.X;
                    int dy = newPos.Y - this.Location.Y;
                    
                    var oldBounds = this.Bounds;
                    this.Location = newPos;
                    this.Parent?.Invalidate(oldBounds);
                    this.Parent?.Update();
                    
                    Dragged?.Invoke(this, dx, dy);
                }
            }
            else
            {
                int m = 10;
                bool top = clientPos.Y < m, bot = clientPos.Y > this.Height - m;
                bool left = clientPos.X < m, right = clientPos.X > this.Width - m;

                if (top && left) { currentResizeDir = ResizeDir.TopLeft; control.Cursor = Cursors.SizeNWSE; }
                else if (top && right) { currentResizeDir = ResizeDir.TopRight; control.Cursor = Cursors.SizeNESW; }
                else if (bot && left) { currentResizeDir = ResizeDir.BottomLeft; control.Cursor = Cursors.SizeNESW; }
                else if (bot && right) { currentResizeDir = ResizeDir.BottomRight; control.Cursor = Cursors.SizeNWSE; }
                else if (top) { currentResizeDir = ResizeDir.Top; control.Cursor = Cursors.SizeNS; }
                else if (bot) { currentResizeDir = ResizeDir.Bottom; control.Cursor = Cursors.SizeNS; }
                else if (left) { currentResizeDir = ResizeDir.Left; control.Cursor = Cursors.SizeWE; }
                else if (right) { currentResizeDir = ResizeDir.Right; control.Cursor = Cursors.SizeWE; }
                else { currentResizeDir = ResizeDir.None; control.Cursor = Cursors.Default; }
            }
        }

        private void BoxControl_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool wasDraggingOrResizing = isDragging || isResizing;
                isDragging = false;
                isResizing = false;

                if (wasDraggingOrResizing)
                {
                    Dropped?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void InitializeColorMenu()
        {
            colorMenu = new ContextMenuStrip();
            for (int i = 0; i < colorOptions.Length; i++)
            {
                var color = colorOptions[i];
                var item = new ToolStripMenuItem { Text = $"色 {i + 1}", BackColor = color, Tag = color };
                item.Click += ColorMenuItem_Click;
                colorMenu.Items.Add(item);
            }

            var deleteItem = new ToolStripMenuItem("このボックスを削除");
            deleteItem.Click += (s, e) => { this.Parent?.Controls.Remove(this); this.Dispose(); };
            colorMenu.Items.Add(new ToolStripSeparator());
            colorMenu.Items.Add(deleteItem);
        }

        private void ColorMenuItem_Click(object? sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is Color selectedColor)
            {
                this.BackColor = selectedColor;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Brush brush = new SolidBrush(Color.FromArgb(100, Color.Black)))
            {
                e.Graphics.FillRectangle(brush, new Rectangle(0, 0, this.Width, headerLabel.Height));
            }
            using (Pen pen = new Pen(Color.Black, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        public string BoxText
        {
            get => contentLabel.Text;
            set => contentLabel.Text = value;
        }

        public new Color BackColor
        {
            get => baseBackColor;
            set
            {
                baseBackColor = value;
                base.BackColor = Color.FromArgb(150, value);
            }
        }
    }
}