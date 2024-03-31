using System.Linq;
using System.Numerics;
using Content.Client.Guidebook.Richtext;
using Content.Client.UserInterface.Controls;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.MedicalScanner;
using Content.Shared.Nutrition.Components;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.HealthAnalyzer.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class HealthAnalyzerWindow : FancyWindow
    {
        private readonly IEntityManager _entityManager;
        private readonly SpriteSystem _spriteSystem;
        private readonly IPrototypeManager _prototypes;
        private readonly IResourceCache _cache;

        //private const int AnalyzerHeight = 430;
        //private const int AnalyzerWidth = 600;

        public HealthAnalyzerWindow()
        {
            RobustXamlLoader.Load(this);

            var dependencies = IoCManager.Instance!;
            _entityManager = dependencies.Resolve<IEntityManager>();
            _spriteSystem = _entityManager.System<SpriteSystem>();
            _prototypes = dependencies.Resolve<IPrototypeManager>();
            _cache = dependencies.Resolve<IResourceCache>();
        }

        public void Populate(HealthAnalyzerScannedUserMessage msg)
        {
            GroupsContainer.RemoveAllChildren();

            var target = _entityManager.GetEntity(msg.TargetEntity);

            if (target == null
                || !_entityManager.TryGetComponent<DamageableComponent>(target, out var damageable))
            {
                NoPatientDataText.Visible = true;
                return;
            }

            NoPatientDataText.Visible = false;

            string entityName = Loc.GetString("health-analyzer-window-entity-unknown-text");
            if (_entityManager.HasComponent<MetaDataComponent>(target.Value))
            {
                entityName = Identity.Name(target.Value, _entityManager);
            }

            if (msg.ScanMode.HasValue)
            {
                ScanModePanel.Visible = true;
                ScanModeText.Text = Loc.GetString(msg.ScanMode.Value
                    ? "health-analyzer-window-scan-mode-active"
                    : "health-analyzer-window-scan-mode-inactive");
                ScanModeText.FontColorOverride = msg.ScanMode.Value ? Color.Yellow : Color.Red;
            }
            else
            {
                ScanModePanel.Visible = false;
            }

            PatientName.Text = Loc.GetString(
                "health-analyzer-window-entity-health-text",
                ("entityName", entityName)
            );

            Temperature.Text = Loc.GetString("health-analyzer-window-entity-temperature-text",
                ("temperature",
                    float.IsNaN(msg.Temperature) ? "N/A" : $"{msg.Temperature - 273f:F1} °C ({msg.Temperature:F1} °K)")
            );

            BloodLevel.Text = Loc.GetString("health-analyzer-window-entity-blood-level-text",
                ("bloodLevel", float.IsNaN(msg.BloodLevel) ? "N/A" : $"{msg.BloodLevel * 100:F1} %")
            );

            if (msg.Bleeding == true)
            {
                Bleeding.Text = Loc.GetString("health-analyzer-window-entity-bleeding-text");
                Bleeding.FontColorOverride = Color.Red;
            }
            else
            {
                Bleeding.Text = string.Empty; // Clear the text
            }

            patientDamageAmount.Text = Loc.GetString(
                "health-analyzer-window-entity-damage-total-text",
                ("amount", damageable.TotalDamage)
            );

            var damageSortedGroups =
                damageable.DamagePerGroup.OrderBy(damage => damage.Value)
                    .ToDictionary(x => x.Key, x => x.Value);
            IReadOnlyDictionary<string, FixedPoint2> damagePerType = damageable.Damage.DamageDict;

            DrawDiagnosticGroups(damageSortedGroups, damagePerType);

            if (_entityManager.TryGetComponent(target, out HungerComponent? hunger)
                && hunger.StarvationDamage != null
                && hunger.CurrentThreshold <= HungerThreshold.Starving)
            {
                var box = new Control { Margin = new Thickness(0, 0, 0, 15) };

                box.AddChild(CreateDiagnosticGroupTitle(
                    Loc.GetString("health-analyzer-window-malnutrition"),
                    "malnutrition"));

                GroupsContainer.AddChild(box);
            }

            if (msg.SurgeryData!.Value.LocalizedName != "")
            {
                var step = msg.SurgeryData.Value;

                SurgeryNameLabel.Text =
                    Loc.GetString("health-analyzer-window-currentOperation") + " " + step.OperationName;
                SurgeryProcedureLabel.Text = Loc.GetString("health-analyzer-window-instructions") + " ";
                SurgeryStep.Text = step.LocalizedName;
                SurgeryStepDesc.Text = step.LocalizedDescription;
                if (step.Icon != null)
                    SurgeryIcon.Texture = _cache.GetResource<TextureResource>(step.Icon);
            }
            else
            {
                SurgeryNameLabel.Text = Loc.GetString("health-analyzer-window-noOperation");
                SurgeryProcedureLabel.Text = "";
                SurgeryStep.Text = "";
                SurgeryStepDesc.Text = "";
                SurgeryIcon.Texture = Texture.Transparent;
            }

            //SetHeight = AnalyzerHeight;
            //SetWidth = AnalyzerWidth;
        }

        private void DrawDiagnosticGroups(
            Dictionary<string, FixedPoint2> groups, IReadOnlyDictionary<string, FixedPoint2> damageDict)
        {
            HashSet<string> shownTypes = new();
            var index = 0;
            var container1 = new Box
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 15),
                Align = BoxContainer.AlignMode.Begin
            };
            var container2 = new Box
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 15),
                Align = BoxContainer.AlignMode.Begin
            };
            var container3 = new Box
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 15),
                Align = BoxContainer.AlignMode.Begin
            };
            // Show the total damage and type breakdown for each damage group.
            foreach (var (damageGroupId, damageAmount) in groups.Reverse())
            {
                if (damageAmount == 0)
                    continue;

                var groupTitleText =
                    $"{Loc.GetString("health-analyzer-window-damage-group-text", ("damageGroup", Loc.GetString("health-analyzer-window-damage-group-" + damageGroupId)), ("amount", damageAmount))}";

                var groupContainer = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                };
                switch (index)
                {
                    case <= 2:
                        container1.AddChild(CreateDiagnosticGroupTitle(groupTitleText, damageGroupId));
                        break;
                    case <= 4:
                        container2.AddChild(CreateDiagnosticGroupTitle(groupTitleText, damageGroupId));
                        break;
                    default:
                        container3.AddChild(CreateDiagnosticGroupTitle(groupTitleText, damageGroupId));
                        break;
                }


                // Show the damage for each type in that group.
                /*
                var group = _prototypes.Index<DamageGroupPrototype>(damageGroupId);

                foreach (var type in group.DamageTypes)
                {
                    if (!damageDict.TryGetValue(type, out var typeAmount) || typeAmount <= 0)
                        continue;
                    // If damage types are allowed to belong to more than one damage group,
                    // they may appear twice here. Mark them as duplicate.
                    if (shownTypes.Contains(type))
                        continue;

                    shownTypes.Add(type);

                    var damageString = Loc.GetString(
                        "health-analyzer-window-damage-type-text",
                        ("damageType", Loc.GetString("health-analyzer-window-damage-type-" + type)),
                        ("amount", typeAmount)
                    );

                    groupContainer.AddChild(CreateDiagnosticItemLabel(damageString.Insert(0, "- ")));
                }
                */

                index++;
            }

            GroupsContainer.Orientation = BoxContainer.LayoutOrientation.Horizontal;
            GroupsContainer.AddChild(container1);
            GroupsContainer.AddChild(container2);
            GroupsContainer.AddChild(container3);
        }

        private Texture GetTexture(string texture)
        {
            var rsiPath = new ResPath("/Textures/Objects/Devices/health_analyzer.rsi");
            var rsiSprite = new SpriteSpecifier.Rsi(rsiPath, texture);

            var rsi = _cache.GetResource<RSIResource>(rsiSprite.RsiPath).RSI;
            if (!rsi.TryGetState(rsiSprite.RsiState, out var state))
            {
                rsiSprite = new SpriteSpecifier.Rsi(rsiPath, "unknown");
            }

            return _spriteSystem.Frame0(rsiSprite);
        }

        private static Label CreateDiagnosticItemLabel(string text)
        {
            return new Label
            {
                Margin = new Thickness(2, 2),
                Text = text,
            };
        }

        private BoxContainer CreateDiagnosticGroupTitle(string text, string id)
        {
            var rootContainer = new BoxContainer
            {
                VerticalAlignment = VAlignment.Bottom,
                Orientation = BoxContainer.LayoutOrientation.Horizontal
            };

            rootContainer.AddChild(new TextureRect
            {
                Margin = new Thickness(0, 3),
                SetSize = new Vector2(30, 30),
                Texture = GetTexture(id.ToLower())
            });

            rootContainer.AddChild(CreateDiagnosticItemLabel(text));

            return rootContainer;
        }
    }
}
