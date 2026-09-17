using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PartComparisionTool.Models;
using Tekla.Structures.Filtering;
using Tekla.Structures.Filtering.Categories;
using Tekla.Structures.Model;

namespace PartComparisionTool.Services
{
    internal sealed class PartComparisonService
    {
        private const double LengthTolerance = 1.0;

        private readonly Model _model;
        private readonly AssemblySnapshotBuilder _snapshotBuilder;
        private readonly AssemblyComparer _comparer;

        public PartComparisonService()
        {
            _model = new Model();
            _snapshotBuilder = new AssemblySnapshotBuilder();
            _comparer = new AssemblyComparer();
        }

        public bool IsConnected => _model.GetConnectionStatus();

        public List<ComparisonResult> FindMatches(
            HashSet<int> phases,
            Action<SearchProgress> progress,
            out string targetDescription)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Open Tekla Structures 2023 with the required model loaded first.");

            if (phases == null || phases.Count == 0)
                throw new InvalidOperationException("Enter at least one phase to search.");

            var selectedPart = GetSelectedPart();
            var targetAssembly = selectedPart.GetAssembly();
            if (targetAssembly == null)
                throw new InvalidOperationException("The selected part does not belong to an assembly.");

            progress?.Invoke(new SearchProgress { Message = "Reading selected assembly..." });
            var target = _snapshotBuilder.Build(targetAssembly);
            targetDescription = BuildTargetDescription(target);

            progress?.Invoke(new SearchProgress
            {
                Message = $"Filtering phases {string.Join(", ", phases.OrderBy(x => x))} for {target.MainProfile}..."
            });

            // Push the cheap phase + profile filtering into Tekla itself. This avoids
            // enumerating every Part in a large model just to reject it in C#.
            var filteredSets = phases
                .OrderBy(x => x)
                .Select(phase => GetPotentialParts(phase, target.MainProfile))
                .ToList();

            var total = filteredSets.Sum(x => x.GetSize());
            var current = 0;
            var results = new List<ComparisonResult>();
            var seenAssemblies = new HashSet<int>();

            foreach (var objects in filteredSets)
            {
                while (objects.MoveNext())
                {
                    current++;
                    var part = objects.Current as Part;
                    if (part == null)
                        continue;

                    if (current == 1 || current % 50 == 0 || current == total)
                    {
                        progress?.Invoke(new SearchProgress
                        {
                            Current = current,
                            Total = total,
                            Message = $"Checking filtered parts {current:n0} of {total:n0}..."
                        });
                    }

                    var length = AssemblySnapshotBuilder.GetLength(part);
                    if (Math.Abs(length - target.MainLength) > LengthTolerance)
                        continue;

                    var assembly = part.GetAssembly();
                    if (assembly == null || assembly.Identifier.ID == targetAssembly.Identifier.ID)
                        continue;

                    // We only want assemblies whose MAIN part matches the requested
                    // profile/length. A fitting with the same profile must not qualify.
                    var mainPart = assembly.GetMainPart() as Part;
                    if (mainPart == null || mainPart.Identifier.ID != part.Identifier.ID)
                        continue;

                    if (!seenAssemblies.Add(assembly.Identifier.ID))
                        continue;

                    progress?.Invoke(new SearchProgress
                    {
                        Current = current,
                        Total = total,
                        Message = $"Comparing candidate {SafeAssemblyMark(mainPart)}..."
                    });

                    try
                    {
                        var candidate = _snapshotBuilder.Build(assembly);
                        results.Add(_comparer.Compare(target, candidate));
                    }
                    catch
                    {
                        // A malformed or unsupported object should not stop the whole site search.
                    }
                }
            }

            var ordered = results
                .OrderBy(x => x.Quality)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => Math.Abs(x.WeightDifference))
                .ThenBy(x => x.Phase)
                .ThenBy(x => x.AssemblyMark)
                .ToList();

            progress?.Invoke(new SearchProgress
            {
                Current = total,
                Total = total,
                Message = ordered.Count == 0
                    ? "No assemblies with the same main profile and length were found in the selected phases."
                    : $"Found {ordered.Count:n0} candidate assembly/assemblies."
            });

            return ordered;
        }

        public void SelectResult(ComparisonResult result)
        {
            if (result?.MainPart == null)
                return;

            var selected = new ArrayList { result.MainPart };
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            selector.Select(selected);
        }

        private ModelObjectEnumerator GetPotentialParts(int phase, string profile)
        {
            var phaseExpression = new BinaryFilterExpression(
                new PartFilterExpressions.Phase(),
                NumericOperatorType.IS_EQUAL,
                new NumericConstantFilterExpression(phase));

            var profileExpression = new BinaryFilterExpression(
                new PartFilterExpressions.Profile(),
                StringOperatorType.IS_EQUAL,
                new StringConstantFilterExpression(profile));

            var filter = new BinaryFilterExpressionCollection();
            filter.Add(new BinaryFilterExpressionItem(
                phaseExpression,
                BinaryFilterOperatorType.BOOLEAN_AND));
            filter.Add(new BinaryFilterExpressionItem(profileExpression));

            return _model.GetModelObjectSelector().GetObjectsByFilter(filter);
        }

        private static Part GetSelectedPart()
        {
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            var selectedObjects = selector.GetSelectedObjects();

            while (selectedObjects.MoveNext())
            {
                var part = selectedObjects.Current as Part;
                if (part != null)
                    return part;
            }

            throw new InvalidOperationException("Select a part in the Tekla model first.");
        }

        private static string BuildTargetDescription(AssemblySnapshot target)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Target {0} | {1} | {2:0} mm | {3:0.0} kg | {4} fitting(s)",
                string.IsNullOrWhiteSpace(target.AssemblyMark) ? target.MainPartMark : target.AssemblyMark,
                target.MainProfile,
                target.MainLength,
                target.Weight,
                target.SecondaryParts.Count);
        }

        private static string SafeAssemblyMark(Part mainPart)
        {
            var value = string.Empty;
            mainPart.GetReportProperty("ASSEMBLY_POS", ref value);
            return string.IsNullOrWhiteSpace(value) ? mainPart.Identifier.ID.ToString() : value;
        }
    }
}
