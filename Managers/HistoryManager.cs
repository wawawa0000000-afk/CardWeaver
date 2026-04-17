using System.Collections.Generic;

namespace CardWeaver.Managers
{
    /// <summary>
    /// アプリケーション全体の操作ミスの取り消し（Undo / Redo）を管理するクラスです。
    /// 参照崩壊を防ぐため、オブジェクトの部分更新（差分パッチ）ではなく、
    /// JSON文字列による「スナップショットの完全保存・完全リストア」方式を採用しています（可用性・信頼性の担保）。
    /// </summary>
    public class HistoryManager
    {
        private List<string> history = new List<string>();
        private int currentIndex = -1;

        public bool CanUndo => currentIndex > 0;
        public bool CanRedo => currentIndex < history.Count - 1;

        public void SaveState(string currentStateJson)
        {
            if (currentIndex >= 0 && currentIndex < history.Count && history[currentIndex] == currentStateJson)
            {
                return;
            }

            if (currentIndex < history.Count - 1)
            {
                history.RemoveRange(currentIndex + 1, history.Count - (currentIndex + 1));
            }

            history.Add(currentStateJson);
            currentIndex++;
        }

        public string? Undo()
        {
            if (CanUndo)
            {
                currentIndex--;
                return history[currentIndex];
            }
            return null;
        }

        public string? Redo()
        {
            if (CanRedo)
            {
                currentIndex++;
                return history[currentIndex];
            }
            return null;
        }

        public void Clear()
        {
            history.Clear();
            currentIndex = -1;
        }
    }
}
