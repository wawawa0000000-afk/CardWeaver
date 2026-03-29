using System;
using System.Drawing;
using System.Windows.Forms;

namespace CardWeaver.Controls
{
    public class CardControl : UserControl
    {
        private Label titleLabel;
        private RichTextBox contentBox;
        private TextBox titleEditBox;

        private Point dragOffset;
        private bool isDragging = false;
        private bool isResizing = false;
        private Point resizeStart;

        private ContextMenuStrip? colorMenu;

        public event EventHandler? CardClicked;
        public event EventHandler? Dropped;

        private const int RESIZE_HANDLE_SIZE = 10;
        private Rectangle resizeHandle;

        private static readonly Color[] colorOptions = new Color[]
        {
            Color.LightYellow, Color.LightBlue, Color.LightGreen, Color.LightPink,
            Color.LightGray, Color.LightCoral, Color.LightCyan, Color.LightGoldenrodYellow,
            Color.LightSalmon, Color.LightSeaGreen, Color.LightSkyBlue, Color.LightSteelBlue,
            Color.MistyRose, Color.PaleGreen, Color.PeachPuff, Color.Thistle
        };

        public Size BaseSize { get; set; } = new Size(200, 120);
        public float CurrentZoom { get; set; } = 1.0f;

        public CardControl()
        {
            this.Size = new Size(200, 120);
            this.BackColor = Color.LightYellow;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.DoubleBuffered = true;

            titleLabel = new Label
            {
                Text = "カードタイトル",
                Dock = DockStyle.Top,
                Font = new Font("Meiryo", 10, FontStyle.Bold),
                Height = 32,
                Padding = new Padding(0, 8, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };

            titleLabel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int r = 5;
                int cx = titleLabel.Width / 2;
                int cy = 6;
                var rect = new Rectangle(cx - r, cy - r, r * 2, r * 2);

                using (var brush = new SolidBrush(Color.DeepSkyBlue))
                    e.Graphics.FillEllipse(brush, rect);
                using (var pen = new Pen(Color.Black, 1.0f))
                    e.Graphics.DrawEllipse(pen, rect);
            };

            contentBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Meiryo", 18),
                BorderStyle = BorderStyle.None
            };

            contentBox.Click += (s, e) => this.OnClick(EventArgs.Empty);

            this.MouseDown += CardControl_MouseDown;
            this.MouseMove += CardControl_MouseMove;
            this.MouseUp += CardControl_MouseUp;

            titleLabel.MouseDown += CardControl_MouseDown;
            titleLabel.MouseMove += CardControl_MouseMove;
            titleLabel.MouseUp += CardControl_MouseUp;

            titleEditBox = new TextBox
            {
                Visible = false,
                Dock = DockStyle.Top,
                Font = new Font("Meiryo", 10, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center
            };
            titleEditBox.Leave += (s, e) => EndTitleEdit();
            titleEditBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) EndTitleEdit(e.KeyCode == Keys.Enter); };

            this.Controls.Add(titleEditBox);
            this.Controls.Add(contentBox);
            this.Controls.Add(titleLabel);

            titleLabel.DoubleClick += (s, e) => StartTitleEdit();

            InitializeColorMenu();
            this.ContextMenuStrip = colorMenu;
            contentBox.ContextMenuStrip = colorMenu;
        }

        private void StartTitleEdit()
        {
            titleEditBox.Text = titleLabel.Text;
            titleLabel.Visible = false;
            titleEditBox.Visible = true;
            titleEditBox.BringToFront();
            titleEditBox.Focus();
        }

        private void EndTitleEdit(bool save = true)
        {
            if (save && !string.IsNullOrWhiteSpace(titleEditBox.Text)) 
                titleLabel.Text = titleEditBox.Text;
            titleEditBox.Visible = false;
            titleLabel.Visible = true;
        }

        private void CardControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                resizeHandle = new Rectangle(
                    this.Width - RESIZE_HANDLE_SIZE,
                    this.Height - RESIZE_HANDLE_SIZE,
                    RESIZE_HANDLE_SIZE,
                    RESIZE_HANDLE_SIZE
                );

                if (resizeHandle.Contains(e.Location))
                {
                    isResizing = true;
                    resizeStart = e.Location;
                }
                else
                {
                    isDragging = true;
                    dragOffset = e.Location;
                    this.BringToFront();
                }
            }
        }

        private void CardControl_MouseMove(object? sender, MouseEventArgs e)
        {
            if (sender is not Control control)
                return;

            resizeHandle = new Rectangle(
                this.Width - RESIZE_HANDLE_SIZE,
                this.Height - RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE
            );

            if (resizeHandle.Contains(e.Location))
            {
                this.Cursor = Cursors.SizeNWSE;
            }
            else
            {
                this.Cursor = Cursors.Default;
            }

            if (isResizing)
            {
                int deltaX = e.X - resizeStart.X;
                int deltaY = e.Y - resizeStart.Y;

                int newWidth = Math.Max(100, this.Width + deltaX);
                int newHeight = Math.Max(80, this.Height + deltaY);

                var oldBounds = this.Bounds;
                this.Size = new Size(newWidth, newHeight);
                this.BaseSize = new Size((int)(newWidth / CurrentZoom), (int)(newHeight / CurrentZoom));
                resizeStart = e.Location;
                this.Parent?.Invalidate(oldBounds);
                this.Parent?.Update();
            }
            else if (isDragging)
            {
                Point screenPos = control.PointToScreen(e.Location);
                Point parentPos = this.Parent?.PointToClient(screenPos) ?? Point.Empty;
                Point newPos = new Point(parentPos.X - dragOffset.X, parentPos.Y - dragOffset.Y);
                if (newPos != this.Location)
                {
                    var oldBounds = this.Bounds;
                    this.Location = newPos;
                    this.Parent?.Invalidate(oldBounds);
                    this.Parent?.Update();
                }
            }
        }

        private void CardControl_MouseUp(object? sender, MouseEventArgs e)
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
                var item = new ToolStripMenuItem
                {
                    Text = $"色 {i + 1}",
                    BackColor = color,
                    Tag = color
                };
                item.Click += ColorMenuItem_Click;
                colorMenu.Items.Add(item);
            }

            var deleteItem = new ToolStripMenuItem("このカードを削除");
            deleteItem.Click += (s, e) =>
            {
                this.Parent?.Controls.Remove(this);
                this.Dispose();
            };
            colorMenu.Items.Add(new ToolStripSeparator());
            colorMenu.Items.Add(deleteItem);
        }

        private void ColorMenuItem_Click(object? sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is Color selectedColor)
            {
                this.BackColor = selectedColor;
                contentBox.BackColor = selectedColor;
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            CardClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // リサイズハンドルを描画
            resizeHandle = new Rectangle(
                this.Width - RESIZE_HANDLE_SIZE,
                this.Height - RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE
            );

            using (Brush brush = new SolidBrush(Color.DarkGray))
            {
                e.Graphics.FillRectangle(brush, resizeHandle);
            }

            using (Pen pen = new Pen(Color.Black, 1))
            {
                e.Graphics.DrawRectangle(pen, resizeHandle);
            }
        }

        public string TitleText
        {
            get => titleLabel.Text;
            set => titleLabel.Text = value;
        }

        public string CardText
        {
            get => contentBox.Text;
            set => contentBox.Text = value;
        }
    }
}