using System;
using System.Drawing;
using System.Windows.Forms;
using CardWeaver.Controls;

namespace CardWeaver.Forms
{
    /// <summary>
    /// メインフォームにおける、コントロールへのイベント付与やショートカット、
    /// オートアライメント（自動整列）等の機能的アクションを切り出した部分です。
    /// </summary>
    public partial class FormMain
    {
        /// <summary>
        /// 新規生成されたカードに対して、UI操作用のコールバックイベントを割り当てます。
        /// Undo（元に戻す）の検知として、DataChanged発火時に SaveStateToHistory() を呼んでいます。
        /// 新規カード要素の拡張時は、ここに忘れずにイベントフックを足してください。
        /// </summary>
        private void AttachCardEvents(CardControl card)
        {
            card.CardClicked += Card_Clicked;
            card.LocationChanged += (s, e) => InvalidateOverlay(true);
            card.SizeChanged += (s, e) => InvalidateOverlay(true);
            
            card.Dropped += (s, e) => 
            {
                RebuildHierarchy();
                SaveStateToHistory();
            };
            
            card.ComponentActivated += (s, e) => UpdateZOrder(card);
            
            card.DataChanged += (s, e) => 
            {
                SaveStateToHistory();
            };
        }

        private void AttachBoxEvents(BoxControl box)
        {
            box.Dropped += (s, e) => 
            {
                RebuildHierarchy();
                SaveStateToHistory();
            };
            
            box.Dragged += Box_Dragged;
            box.ComponentActivated += (s, e) => UpdateZOrder(box);
            
            box.DataChanged += (s, e) => 
            {
                SaveStateToHistory();
            };
        }

        /// <summary>
        /// 現在のキャンバスにある全てのカードとボックスの情報を集約し、
        /// JSON スナップショットとして `historyManager` に押し込み（Push）します。
        /// 巨大なキャンバスでも一瞬で終わるよう設計されていますが、過度な連発はメモリを圧迫し得るので
        /// UI変更の「確定後」に呼ぶようにしてください。
        /// </summary>
        public void SaveStateToHistory()
        {
            var cards = this.Controls.OfType<CardControl>();
            string json = Managers.WorkspaceManager.SerializeWorkspace(cards, boxes, connections);
            historyManager.SaveState(json);
        }

        private void PerformUndo()
        {
            string? json = historyManager.Undo();
            if (json != null)
            {
                LoadWorkspaceFromJson(json);
            }
        }

        private void PerformRedo()
        {
            string? json = historyManager.Redo();
            if (json != null)
            {
                LoadWorkspaceFromJson(json);
            }
        }
        
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                PerformUndo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
            {
                PerformRedo();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        
        /// <summary>
        /// ボックスの「現在の横幅」に基づき数学的にピッタリ敷き詰める格子パッキング（GridLayout）と、
        /// ボックス外のカードを安全地帯に避難させる整列を行います。
        /// ユーザーに強固な空間記憶リセットを強いるため、常に実行前後に履歴を保存し、
        /// Ctrl+Z (Undo) で直ぐに元に戻せる措置を取っています。
        /// </summary>
        private void AutoAlignCards()
        {
            SaveStateToHistory(); // Save state before aligning
            
            RebuildHierarchy();
            this.SuspendLayout();

            var allCards = this.Controls.OfType<CardControl>().ToList();
            var allBoxes = this.Controls.OfType<BoxControl>().ToList();
            
            int cardMargin = 15;
            int boxHeaderHeight = 35; // ボックスのヘッダー（タイトル用の余白）

            // 1. ボックス内（所属カード）のグリッド・パッキング
            foreach (var box in allBoxes)
            {
                var boxedCards = allCards.Where(c => parentMap.ContainsKey(c) && parentMap[c] == box)
                                         .OrderBy(c => c.Location.Y)
                                         .ThenBy(c => c.Location.X)
                                         .ToList();
                
                if (boxedCards.Count == 0) continue;

                int firstCardWidth = boxedCards[0].Width;
                int availableWidth = box.Width - (cardMargin * 2);
                int expectedCardSpacingX = firstCardWidth + cardMargin;
                
                // ボックス幅から逆算して、何列入るかを決定
                int cols = Math.Max(1, availableWidth / expectedCardSpacingX);
                
                int currentX = cardMargin;
                int currentY = boxHeaderHeight;
                int rowMaxHeight = 0;
                int maxRequiredX = 0;

                for (int i = 0; i < boxedCards.Count; i++)
                {
                    var card = boxedCards[i];
                    
                    if (i > 0 && i % cols == 0)
                    {
                        // 改行
                        currentX = cardMargin;
                        currentY += rowMaxHeight + cardMargin;
                        rowMaxHeight = 0;
                    }

                    // グリッド数学に基づくローカル座標を、絶対座標として設定する
                    card.Location = new Point(box.Location.X + currentX, box.Location.Y + currentY);
                    
                    if (card.Height > rowMaxHeight)
                    {
                        rowMaxHeight = card.Height;
                    }
                    
                    currentX += card.Width + cardMargin;
                    if (currentX > maxRequiredX)
                    {
                        maxRequiredX = currentX;
                    }
                }
                
                int requiredHeight = currentY + rowMaxHeight + cardMargin;
                int newBoxWidth = Math.Max(box.Width, maxRequiredX);
                int newBoxHeight = Math.Max(box.Height, requiredHeight);

                // はみ出す場合はボックスのサイズを広げる
                if (box.Width != newBoxWidth || box.Height != newBoxHeight)
                {
                    box.Size = new Size(newBoxWidth, newBoxHeight);
                    if (box.CurrentZoom > 0)
                    {
                        box.BaseSize = new Size((int)(newBoxWidth / box.CurrentZoom), (int)(newBoxHeight / box.CurrentZoom));
                    }
                }
            }

            // 2. ボックスに属さないフリーのカードの整列
            var freeCards = allCards.Where(c => !parentMap.ContainsKey(c))
                                    .OrderBy(c => c.Location.Y)
                                    .ThenBy(c => c.Location.X)
                                    .ToList();

            if (freeCards.Count > 0)
            {
                // ボックスの下側に配置する
                int startY = 80;
                if (allBoxes.Count > 0)
                {
                    startY = allBoxes.Max(b => b.Bottom) + 50;
                }
                
                int startX = 50;
                int screenWidth = this.ClientSize.Width > 0 ? this.ClientSize.Width : 1000;
                
                int cx = startX;
                int cy = startY;
                int rowMaxH = 0;
                
                foreach (var card in freeCards)
                {
                    if (cx + card.Width > screenWidth - cardMargin && cx > startX)
                    {
                        cx = startX;
                        cy += rowMaxH + cardMargin;
                        rowMaxH = 0;
                    }
                    
                    card.Location = new Point(cx, cy);
                    if (card.Height > rowMaxH) rowMaxH = card.Height;
                    cx += card.Width + cardMargin;
                }
            }
            
            this.ResumeLayout();
            RebuildHierarchy();
            InvalidateOverlay(true);
            SaveStateToHistory(); // 整列後の状態を履歴に保存
        }
    }
}
