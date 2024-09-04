using System.Xml.Linq;
using WouterVanRanst.Utils.Builders;

namespace SolutionOptimizer;

internal static class Program
{
    static void Main(string[] args)
    {
        var solutionDir = @"C:\Repos\NetRelatieRegister";
        var projects = LoadProjects(solutionDir);

        var dependencyGraph = BuildDependencyGraph(projects);

        // Get the tiers of projects to clean
        var cleanupOrder = GetCleanupTiers(dependencyGraph);

        var md = ToMermaid(cleanupOrder);


        // Display the results
        //cleanupOrder.Dump("Cleanup Order Tiers");


    }

    // Function to load all the .csproj files from the directory and parse project references and package references
    static IEnumerable<Project> LoadProjects(string solutionDir)
    {
        return Directory.EnumerateFiles(solutionDir, "*.csproj", SearchOption.AllDirectories)
            .Select(csprojPath => new Project(csprojPath));
    }

    // Builds a dependency graph where each project references other projects it depends on
    static Dictionary<Project, List<Project>> BuildDependencyGraph(IEnumerable<Project> projects)
    {
        var projectDict = projects.ToDictionary(p => p.Name, p => p);
        var dependencyGraph = new Dictionary<Project, List<Project>>();

        foreach (var project in projects)
        {
            var dependencies = project.ProjectReferences
                .Where(dep => projectDict.ContainsKey(dep))
                .Select(dep => projectDict[dep])
                .ToList();

            dependencyGraph[project] = dependencies;
        }

        return dependencyGraph;
    }

    // Uses a topological sort to determine the order of projects for cleanup
    static  List<List<Project>> GetCleanupTiers(Dictionary<Project, List<Project>> dependencyGraph)
    {
        var tiers = new List<List<Project>>();
        var projectInDegree = dependencyGraph.ToDictionary(p => p.Key, p => 0);

        // Calculate in-degrees (number of incoming edges) for each project
        foreach (var kvp in dependencyGraph)
        {
            foreach (var dep in kvp.Value)
            {
                projectInDegree[dep]++;
            }
        }

        // Start with projects that have no dependencies (in-degree 0)
        var zeroInDegreeProjects = new Queue<Project>(projectInDegree.Where(p => p.Value == 0).Select(p => p.Key));

        while (zeroInDegreeProjects.Any())
        {
            var currentTier = new List<Project>();

            foreach (var project in zeroInDegreeProjects.ToList())
            {
                zeroInDegreeProjects.Dequeue();
                currentTier.Add(project);

                // Reduce in-degree for all projects that depend on this one
                foreach (var dep in dependencyGraph[project])
                {
                    projectInDegree[dep]--;
                    if (projectInDegree[dep] == 0)
                    {
                        zeroInDegreeProjects.Enqueue(dep);
                    }
                }
            }

            tiers.Add(currentTier);
        }

        return tiers;
    }

    static string ToMermaid(List<List<Project>> projects)
    {
        // Create a MermaidGraph instance
        var mermaidGraph = new MermaidGraph("LR");
        mermaidGraph.AddHandler<Project, ProjectGraphHandler>();
        mermaidGraph.AddHandler<Tier, TierGraphHandler>();

        for (int i = 0; i < projects.Count; i++)
        {
            var t = new Tier($"Tier {i}");
            mermaidGraph.AddObject(t);

            foreach (var p in projects[i])
            {
                mermaidGraph.AddObject(p, t);
            }
            
        }

        foreach (var project in projects.SelectMany(p => p))
        {
            foreach (var dependency in project.ProjectReferences)
            {
                mermaidGraph.AddEdge(project, projects.SelectMany(p => p).FirstOrDefault(p => p.Name == dependency), "depends on");
            }
        }

        //// Add each project and its dependencies
        //foreach (var project in projects.Keys)
        //{
        //    mermaidGraph.AddObject(project);
        //}

        //foreach (var project in projects)
        //{
        //    foreach (var dependency in projects[project])
        //    {
        //        mermaidGraph.AddEdge(project, dependency, "depends on");
        //    }
        //}

        // Return the Mermaid graph as a string
        return mermaidGraph.ToString();
    }


    // Project class to hold the name, file path, project references, and package references of a project
    public class Project
    {
        public string Name { get; }
        public string FilePath { get; }
        public List<string> ProjectReferences { get; }
        public List<string> PackageReferences { get; }

        public Project(string filePath)
        {
            FilePath = filePath;
            Name = Path.GetFileNameWithoutExtension(filePath);
            ProjectReferences = ParseProjectReferences(filePath);
            PackageReferences = ParsePackageReferences(filePath);
        }

        // Parse the csproj file and extract ProjectReferences
        List<string> ParseProjectReferences(string filePath)
        {
            var doc = XDocument.Load(filePath);

            return doc.Descendants("ProjectReference")
                .Select(x => Path.GetFileNameWithoutExtension((string)x.Attribute("Include")))
                .ToList();
        }

        // Parse the csproj file and extract PackageReferences
        List<string> ParsePackageReferences(string filePath)
        {
            var doc = XDocument.Load(filePath);

            return doc.Descendants("PackageReference")
                .Select(x => (string)x.Attribute("Include"))
                .ToList();
        }

        public override string ToString() => Name;

        public override int GetHashCode() => FilePath.GetHashCode(StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is Project other && FilePath.Equals(other.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    public record Tier(string Name);

    public class ProjectGraphHandler : IGraphObjectHandler
    {
        public void Configure(GraphObject graphObject, object sourceObject)
        {
            var project = (Project)sourceObject;
            graphObject.Key = project.Name.Replace(".", "");
            graphObject.Caption = project.Name;
            // Optionally add more details such as package references or other metadata
        }
    }

    public class TierGraphHandler : IGraphObjectHandler
    {
        public void Configure(GraphObject graphObject, object sourceObject)
        {
            var tier = (Tier)sourceObject;
            graphObject.Key = tier.Name.Replace(".", "").Replace(" ", "");
            graphObject.Caption = tier.Name;
        }
    }
}