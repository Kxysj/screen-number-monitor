using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ScreenWatch
{
    internal static class DigitGeometry
    {
        sealed class Component { public Rectangle Bounds; public int Area; }
        internal static bool CoversVisibleGlyphs(Bitmap bitmap,string text)
        {
            string cleaned=(text??"").Trim();
            if(!Regex.IsMatch(cleaned,@"^[+\-]?[0-9]+(?:[.,][0-9]+)*$")) return true;
            int digitCount=cleaned.Count(char.IsDigit);
            var components=FindComponents(bitmap);
            if(components.Count==0)return false;
            int height=components.Max(c=>c.Bounds.Height);
            var tall=components.Where(c=>c.Bounds.Height>=height*.65 && c.Bounds.Width<=height*1.15 && c.Area>=Math.Max(3,height*height/1000)).ToList();
            if(tall.Count<=digitCount)return true;
            double baseline=tall.Average(c=>c.Bounds.Bottom), top=tall.Average(c=>c.Bounds.Top);
            // More clearly aligned glyphs than recognized digits means OCR dropped
            // part of the selection. Never silently monitor the remaining suffix.
            return tall.Any(c=>Math.Abs(c.Bounds.Bottom-baseline)>height*.18 || Math.Abs(c.Bounds.Top-top)>height*.18);
        }
        // A decimal must exist in the pixels, near the digit baseline and between
        // two aligned glyphs. Whitespace alone is never turned into a decimal.
        internal static string RestoreDecimal(Bitmap bitmap,string text)
        {
            string cleaned=(text??"").Normalize().Trim().Replace('·','.').Replace('•','.').Replace('−','-');
            if(!Regex.IsMatch(cleaned,@"^[+\-]?[^\S\r\n]*\d[\d. \t]*$") || cleaned.Contains("\n")) return text;
            string digits=Regex.Replace(cleaned,@"\D","");
            if(digits.Length<2 || digits.Length>32) return text;
            var components=FindComponents(bitmap);
            if(components.Count<3 || components.Count>35) return text;
            int height=components.Max(c=>c.Bounds.Height);
            var significant=components.Where(c=>c.Area>=Math.Max(3,height*height/1000)).OrderBy(c=>c.Bounds.Left).ToList();
            var tall=significant.Where(c=>c.Bounds.Height>=height*.65).ToList();
            if(tall.Count!=digits.Length || tall.Any(c=>c.Bounds.Width>height*1.15)) return text;
            double baseline=tall.Average(c=>c.Bounds.Bottom), top=tall.Average(c=>c.Bounds.Top);
            if(tall.Any(c=>Math.Abs(c.Bounds.Bottom-baseline)>height*.18 || Math.Abs(c.Bounds.Top-top)>height*.18)) return text;
            var dots=significant.Where(c=>!tall.Contains(c) && c.Bounds.Width<=height*.28 && c.Bounds.Height<=height*.28 && c.Bounds.Width>=c.Bounds.Height*.5 && c.Bounds.Width<=c.Bounds.Height*1.8 && c.Bounds.Bottom>=baseline-height*.18 && c.Bounds.Bottom<=baseline+height*.10).ToList();
            if(dots.Count!=1) return text;
            var dot=dots[0]; int position=tall.Count(c=>c.Bounds.Right<=dot.Bounds.Left);
            if(position==0 || position==tall.Count) return text;
            var left=tall[position-1].Bounds; var right=tall[position].Bounds;
            if(right.Left<dot.Bounds.Right || dot.Bounds.Left-left.Right>height*.7 || right.Left-dot.Bounds.Right>height*.7) return text;
            // Extra marks/units and multiple rows make geometric reconstruction unsafe.
            foreach(var component in significant)
            {
                if(tall.Contains(component) || component==dot) continue;
                var r=component.Bounds;
                if(!(cleaned.StartsWith("-") && r.Right<tall[0].Bounds.Left && r.Width>=r.Height*2.2 && r.Height<height*.25)) return text;
            }
            return (cleaned.StartsWith("-")?"-":cleaned.StartsWith("+")?"+":"")+digits.Insert(position,".");
        }
        static List<Component> FindComponents(Bitmap bitmap)
        {
            int width=bitmap.Width,height=bitmap.Height;
            var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] mask=new byte[width*height];
            try
            {
                byte[] pixels=new byte[data.Stride*height]; Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){int at=y*data.Stride+x*4; if(pixels[at]+pixels[at+1]+pixels[at+2]<420)mask[y*width+x]=1;}
            }
            finally { bitmap.UnlockBits(data); }
            var result=new List<Component>(); var queue=new Queue<int>();
            for(int i=0;i<mask.Length;i++)
            {
                if(mask[i]==0)continue; mask[i]=0;queue.Enqueue(i);
                int left=width,right=0,top=height,bottom=0,area=0;
                while(queue.Count>0)
                {
                    int at=queue.Dequeue(),x=at%width,y=at/width; area++;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    { int nx=x+dx,ny=y+dy;if(nx<0||nx>=width||ny<0||ny>=height)continue;int next=ny*width+nx;if(mask[next]==0)continue;mask[next]=0;queue.Enqueue(next); }
                }
                if(area>=2)result.Add(new Component{Area=area,Bounds=Rectangle.FromLTRB(left,top,right+1,bottom+1)});
            }
            return result;
        }
    }
}
