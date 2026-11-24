using System.Xml.Linq;
using AlgorithmCore;
namespace Web;
internal static class FlowSvg
{
    private static readonly XNamespace S = "http://www.w3.org/2000/svg";
    private static XElement E(string name, params object[] contents) => new(S + name, contents);
    private static XAttribute A(string name, object value) => new(name, value);
    internal static (string Svg, int Width) Render(Flowchart graph)
    {
        int width = graph.Nodes.Max(n => n.X + n.Width) + 70;
        int height = graph.Nodes.Max(n => n.Y + n.Height) + 45;
        var root = E("svg", A("viewBox", $"0 0 {width} {height}"), A("width", width), A("height", height), A("role", "img"), A("aria-label", "Generated algorithm flowchart"), E("title", "Generated algorithm flowchart"));
        root.Add(E("defs", E("marker", A("id", "arrow"), A("viewBox", "0 0 10 10"), A("refX", 9), A("refY", 5), A("markerWidth", 7), A("markerHeight", 7), A("orient", "auto"), E("path", A("d", "M0 0 L10 5 L0 10 Z"), A("fill", "#53657c")))));
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in graph.Edges)
        {
            var a = nodes[edge.Source]; var b = nodes[edge.Target];
            int ax = a.X + a.Width / 2, ay = a.Y + a.Height, bx = b.X + b.Width / 2, by = b.Y;
            string d; int lx, ly;
            if (edge.Source == edge.Target)
            {
                // Empty loop body: draw a visible return from bottom to left.
                int lane = Math.Max(8, a.X - 24);
                int bottom = a.Y + a.Height + 26;
                d = $"M {ax} {ay} V {bottom} H {lane} V {a.Y + a.Height / 2} H {a.X}";
                lx = ax + 8; ly = ay + 18;
            }
            else if (edge.Back || b.Y <= a.Y)
            {
                ax = a.X; ay = a.Y + a.Height / 2; bx = b.X; by = b.Y + b.Height / 2;
                int lane = Math.Max(8, Math.Min(ax,bx) - 24);
                d = $"M {ax} {ay} H {lane} V {by} H {bx}"; lx = lane + 3; ly = ay - 8;
            }
            else
            {
                int mid = (ay + by) / 2;
                if (a.Kind == "decision" && bx != ax) { ax = bx > ax ? a.X + a.Width : a.X; ay = a.Y + a.Height / 2; d = $"M {ax} {ay} H {bx} V {by}"; lx = (ax + bx) / 2; ly = ay - 8; }
                else { d = $"M {ax} {ay} V {mid} H {bx} V {by}"; lx = ax + 10; ly = ay + 18; }
            }
            root.Add(E("path", A("d",d), A("stroke","#53657c"), A("stroke-width",1.7), A("fill","none"), A("marker-end","url(#arrow)")));
            if (!string.IsNullOrEmpty(edge.Label)) root.Add(E("text",A("x",lx),A("y",ly),A("font-size",12),A("font-family","system-ui"),A("fill","#34465a"),edge.Label));
        }
        foreach (var n in graph.Nodes)
        {
            int x=n.X,y=n.Y,w=n.Width,h=n.Height,cx=x+w/2,cy=y+h/2;
            string fill = n.Kind switch { "terminal"=>"#dcfce7", "decision"=>"#fef3c7", "io"=>"#dbeafe", "unsupported"=>"#fee2e2", _=>"#ffffff" };
            XElement shape = n.Kind switch {
                "decision" => E("polygon",A("points",$"{cx},{y} {x+w},{cy} {cx},{y+h} {x},{cy}")),
                "io" => E("polygon",A("points",$"{x+22},{y} {x+w},{y} {x+w-22},{y+h} {x},{y+h}")),
                _ => E("rect",A("x",x),A("y",y),A("width",w),A("height",h),A("rx",n.Kind=="terminal"?h/2:3))
            };
            shape.Add(A("fill",fill),A("stroke","#334155"),A("stroke-width",1.6));
            if(n.Kind=="unsupported") shape.Add(A("stroke-dasharray","5 3"));
            root.Add(shape);
            var lines = Wrap(n.Label,n.Kind=="decision"?24:34);
            int maxLines = Math.Max(1,(h-20)/16);
            if(n.Kind=="decision") maxLines = Math.Max(1,(h/2)/16);
            if(lines.Count>maxLines) { lines=lines.Take(maxLines).ToList(); lines[^1]=lines[^1].TrimEnd()+"…"; }
            var txt=E("text",A("x",cx),A("y",cy-(lines.Count-1)*8+5),A("text-anchor","middle"),A("font-family","Consolas, monospace"),A("font-size",13),A("fill","#17263c"),E("title",n.Label));
            for(int i=0;i<lines.Count;i++) txt.Add(E("tspan",A("x",cx),A("dy",i==0?0:16),lines[i]));
            root.Add(txt);
        }
        return (root.ToString(SaveOptions.DisableFormatting),width);
    }
    private static List<string> Wrap(string text,int max)
    {
        var lines=new List<string>();
        foreach(var raw in text.Replace("\r","").Split('\n')) { string s=raw; while(s.Length>max) { int cut=s.LastIndexOf(' ',max); if(cut<=0) cut=max; lines.Add(s[..cut]); s=s[cut..].TrimStart(); } lines.Add(s); }
        return lines;
    }
}
