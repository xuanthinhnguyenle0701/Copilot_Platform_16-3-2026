using System.Collections.Generic;

namespace Middleware_console 
{
    // --- 1. CLASS TRÙM: QUẢN LÝ CẢ DỰ ÁN ---
    public class ScadaProjectModel
    {
        public string ProjectName { get; set; }
        public string DeviceName { get; set; }
        public string StartScreenName { get; set; } 
        public List<ScadaScreenModel> Screens { get; set; } 
    }

    // --- 2. CLASS MÀN HÌNH: GIỮ NGUYÊN NHƯNG SẼ NẰM TRONG LIST ---
    public class ScadaScreenModel
    {
        public string ScreenName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<ScadaLayerModel> Layers { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }

    public class ScadaLayerModel
    {
        public string LayerName { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }

    public class ScadaItemModel
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool? EnableCreation { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; }
        public Dictionary<string, string> Events { get; set; }
        // Đệ quy cho các vật thể con (Group/Container)
        public List<ScadaItemModel> Items { get; set; } 
        public string BindTag { get; set; }
        public string TagName { get; set; }
        public LibraryModel Library { get; set; }
    }

    public class LibraryModel
    {
        public string LibraryPath { get; set; }
        public string SubLibrary { get; set; }
    }
}