using CardWeaver.Models;
using CardWeaver.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CardWeaver.Managers
{
    /// <summary>
    /// メインプロセス（セーブ・ロード・Undo）用のUIコンポーネント群を、純粋なデータモデル（WorkspaceData）に相互変換するマネージャークラスです。
    /// OSS開発者へ:
    /// - もし Canvas 上に「新しい独自のコントロール」を追加した場合は、この Manager にシリアライズ用のマッピング処理を書き足してください。
    /// - そうするだけで、UIの変更が JSON に保存され、Undo / Redo 機構にも自動的にフル対応するようになります。
    /// </summary>
    public static class WorkspaceManager
    {
        public static string SerializeWorkspace(IEnumerable<CardControl> cards, IEnumerable<BoxControl> boxes, IEnumerable<(CardControl from, CardControl to)> connections)
        {
            var data = new WorkspaceData();
            var cardList = cards.ToList();

            foreach (var card in cardList)
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
                int fromIndex = cardList.IndexOf(conn.from);
                int toIndex = cardList.IndexOf(conn.to);
                if (fromIndex >= 0 && toIndex >= 0)
                {
                    data.Connections.Add(new ConnectionData
                    {
                        FromIndex = fromIndex,
                        ToIndex = toIndex
                    });
                }
            }

            return JsonSerializer.Serialize(data);
        }

        public static WorkspaceData? DeserializeWorkspace(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<WorkspaceData>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
