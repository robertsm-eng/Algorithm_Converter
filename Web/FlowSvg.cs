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
        var routePoints = graph.Edges.SelectMany(e => e.Points).ToList();
        int width = Math.Max(graph.Nodes.Max(n => n.X + n.Width), routePoints.Count == 0 ? 0 : routePoints.Max(p => p.X)) + 45;
        int height = Math.Max(graph.Nodes.Max(n => n.Y + n.Height), routePoints.Count == 0 ? 0 : routePoints.Max(p => p.Y)) + 45;
        var root = E("svg", A("viewBox", $"0 0 {width} {height}"), A("width", width), A("height", height), A("role", "img"), A("aria-label", "Algorithm flowchart"));
        root.Add(E("defs", E("marker", A("id", "arrow"), A("viewBox", "0 0 10 10"), A("refX", 9), A("refY", 5), A("markerWidth", 7), A("markerHeight", 7), A("orient", "auto"), E("path", A("d", "M0 0 L10 5 L0 10 Z"), A("fill", "#53657c")))));
        foreach (var edge in graph.Edges)
        {
            string d = string.Join(" ", edge.Points.Select((p, i) => $"{(i == 0 ? "M" : "L")} {p.X} {p.Y}"));
            root.Add(E("path", A("d", d), A("stroke", "#53657c"), A("stroke-width", 1.7), A("fill", "none"), A("stroke-linejoin", "round"), A("marker-end", "url(#arrow)")));
            if (!string.IsNullOrEmpty(edge.Label)) root.Add(E("text", A("x", edge.LabelX), A("y", edge.LabelY), A("text-anchor", "middle"), A("font-size", 12), A("font-family", "system-ui"), A("fill", "#34465a"), A("paint-order", "stroke"), A("stroke", "#ffffff"), A("stroke-width", 3), A("stroke-linejoin", "round"), edge.Label));
        }
        foreach (var n in graph.Nodes)
        {
            int x=n.X,y=n.Y,w=n.Width,h=n.Height,cx=x+w/2,cy=y+h/2;
            string fill = n.Kind switch { "terminal"=>"#dcfce7", "decision"=>"#fef3c7", "io"=>"#dbeafe", "unsupported"=>"#fee2e2", _=>"#ffffff" };
            XElement shape = n.Kind switch {
                "decision" => E("polygon", A("points", $"{cx},{y} {x+w},{cy} {cx},{y+h} {x},{cy}")),
                "io" => E("polygon", A("points", $"{x+22},{y} {x+w},{y} {x+w-22},{y+h} {x},{y+h}")),
                _ => E("rect", A("x",x), A("y",y), A("width",w), A("height",h), A("rx",n.Kind=="terminal"?h/2:0))
            };
            shape.Add(A("fill",fill),A("stroke","#334155"),A("stroke-width",1.6));
            if(n.Kind=="unsupported") shape.Add(A("stroke-dasharray","5 3"));
            root.Add(shape);
            if (n.Kind == "call")
            {
                int inset = Math.Max(12, (int)(w * .07));
                root.Add(E("path", A("d", $"M {x+inset} {y} V {y+h} M {x+w-inset} {y} V {y+h}"), A("stroke", "#334155"), A("stroke-width", 1.6), A("fill", "none")));
            }
            var lines = n.Lines;
            var txt=E("text", A("x",cx), A("y",cy-(lines.Count-1)*9+5), A("text-anchor","middle"), A("font-family","Consolas, monospace"), A("font-size",13), A("fill","#17263c"), E("title", n.Label));
            for(int i=0;i<lines.Count;i++) txt.Add(E("tspan", A("x",cx), A("dy",i==0?0:18), lines[i]));
            root.Add(txt);
        }
        return (root.ToString(SaveOptions.DisableFormatting),width);
    }
}
