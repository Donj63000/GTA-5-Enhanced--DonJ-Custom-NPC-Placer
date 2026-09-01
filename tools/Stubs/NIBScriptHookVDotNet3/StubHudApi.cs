using System;
using System.Drawing;

namespace GTA.UI
{
    // Je reproduis le contrat minimal du renderer PNG que le HUD Justice charge par réflexion.
    public sealed class CustomSprite : IDisposable
    {
        public CustomSprite(string fileName, SizeF size, PointF position)
        {
            FileName = fileName;
            Size = size;
            Position = position;
            Color = Color.White;
        }

        public string FileName { get; private set; }
        public PointF Position { get; set; }
        public SizeF Size { get; set; }
        public Color Color { get; set; }
        public bool Centered { get; set; }

        public void Draw()
        {
            // Je n'affiche rien dans le stub headless.
        }

        public void Dispose()
        {
            // Je ne possède aucune ressource native dans le stub.
        }
    }
}
