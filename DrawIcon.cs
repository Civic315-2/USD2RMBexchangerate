using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Collections.Generic;
class DrawIcon {
 static void Main() {
  int[] sizes={16,20,24,32,48,64,128,256};
  var images=new List<byte[]>();
  foreach(int size in sizes) {
   using(var bitmap=new Bitmap(size,size))
   using(var g=Graphics.FromImage(bitmap))
   using(var red=new SolidBrush(Color.FromArgb(168,26,51)))
   using(var ring=new Pen(red,8))
   using(var box=new Pen(red,8))
   using(var stream=new MemoryStream()) {
    g.SmoothingMode=SmoothingMode.AntiAlias;
    g.ScaleTransform(size/64f,size/64f);
    g.DrawEllipse(ring,8,8,48,48);
    g.FillRectangle(red,28,8,8,14);
    g.FillRectangle(red,28,42,8,14);
    box.LineJoin=LineJoin.Round;
    g.DrawRectangle(box,22,24,20,16);
    bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
    images.Add(stream.ToArray());
   }
  }
  using(var output=new BinaryWriter(File.Create("work/boc.ico"))) {
   output.Write((short)0); output.Write((short)1); output.Write((short)sizes.Length);
   int offset=6+16*sizes.Length;
   for(int i=0;i<sizes.Length;i++) {
    output.Write((byte)(sizes[i]%256)); output.Write((byte)(sizes[i]%256));
    output.Write((short)0); output.Write((short)1); output.Write((short)32);
    output.Write(images[i].Length); output.Write(offset);
    offset+=images[i].Length;
   }
   foreach(var image in images) output.Write(image);
  }
 }
}
