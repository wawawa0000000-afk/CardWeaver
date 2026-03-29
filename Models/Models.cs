using System;
using System.Collections.Generic;
using System.Drawing;

namespace CardWeaver.Models
{
    /// <summary>
    /// カードの状態を保存するデータモデル
    /// </summary>
    public class CardData
    {
        public Point Location { get; set; }
        public string? ColorName { get; set; }
        public string? Text { get; set; }
        public string? Title { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// ボックスの状態を保存するデータモデル
    /// </summary>
    public class BoxData
    {
        public Point Location { get; set; }
        public string? ColorName { get; set; }
        public string? Text { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// カード間の接続情報を保存するデータモデル
    /// </summary>
    public class ConnectionData
    {
        public int FromIndex { get; set; }
        public int ToIndex { get; set; }
    }

    /// <summary>
    /// ワークスペース全体の状態を保存するデータモデル
    /// </summary>
    public class WorkspaceData
    {
        public List<CardData> Cards { get; set; } = new();
        public List<BoxData> Boxes { get; set; } = new();
        public List<ConnectionData> Connections { get; set; } = new();
    }
}