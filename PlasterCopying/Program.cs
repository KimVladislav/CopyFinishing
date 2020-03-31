using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PlasterCopying
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class Program : IExternalCommand
    {
        public Result Execute(ExternalCommandData revit, ref string message, ElementSet elements)
        {
            var doc = revit.Application.ActiveUIDocument.Document;

            try
            {
                var existingGroups = new FilteredElementCollector(doc).OfClass(typeof(GroupType)).ToElements();
                var existingGroupNames = existingGroups.Select(x => x.Name);

                var existingFinishingGroups = new List<GroupType>();
                foreach (GroupType gt in existingGroups)
                {
                    if (gt.Groups.Size == 0)
                        continue;

                    var groupIterator = gt.Groups.ForwardIterator();
                    groupIterator.MoveNext();

                    var instance = groupIterator.Current as Group;

                    bool isFinishingGroup = true;
                    ElementId sameLevelId = ElementId.InvalidElementId;
                    foreach (var id in instance.GetMemberIds())
                    {
                        var member = doc.GetElement(id);

                        if (!(member is Wall))
                        {
                            isFinishingGroup = false;
                            break;
                        }

                        if (sameLevelId == ElementId.InvalidElementId)
                            sameLevelId = member.LevelId;

                        if (sameLevelId != member.LevelId)
                        {
                            isFinishingGroup = false;
                            break;
                        }
                    }

                    if (isFinishingGroup)
                        existingFinishingGroups.Add(gt);
                }

                var existingFinishingGroupNames = existingFinishingGroups.Select(x => x.Name).ToArray();

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.ProjectElevation).ToList();
                var levelNames = levels.Select(x => x.Name).ToArray();

                Dialog gui = new Dialog();
                gui.GroupsComboBox.Items.AddRange(existingFinishingGroupNames);
                gui.StandartFloorComboBox.Items.AddRange(levelNames);


                gui.StandartFloorComboBox.SelectedIndexChanged += delegate
                {
                    int index = gui.StandartFloorComboBox.SelectedIndex;
                    var level = levels[index];
                    var levelWallTypes = Logic.GetTypesOfWallsForLevel(doc, level.Id);
                    gui.FinishingCheckedListBox.Tag = levelWallTypes.Select(x => x.Id).ToList();

                    gui.FinishingCheckedListBox.Items.Clear();
                    gui.FinishingCheckedListBox.Items.AddRange(levelWallTypes.Select(x => x.Name).ToArray());

                    gui.LevelsCheckedListBox.Items.Clear();
                    for (int i = 0; i < levels.Count; i++)
                    {
                        if (i == index) continue;

                        gui.LevelsCheckedListBox.Items.Add(levels[i].Name);
                    }
                };

                gui.CopyButton.Click += delegate
                {
                    if (string.IsNullOrEmpty(gui.UserGroupNameTextBox.Text) || !NamingUtils.IsValidName(gui.UserGroupNameTextBox.Text))
                    {
                        System.Windows.Forms.MessageBox.Show("Введите корректное имя для группы");
                        return;
                    }

                    if (existingGroupNames.Contains(gui.UserGroupNameTextBox.Text))
                    {
                        System.Windows.Forms.MessageBox.Show("В текущем проекте уже существует группа с таким именем");
                        return;
                    }

                    if (gui.LevelsCheckedListBox.CheckedItems.Count == 0)
                    {
                        System.Windows.Forms.MessageBox.Show("Выберите уровни для копирования выбранных отделочных стен");
                        return;
                    }

                    if (gui.GroupsRadioButton.Checked)
                    {
                        if (gui.GroupsComboBox.SelectedIndex < 0)
                        {
                            System.Windows.Forms.MessageBox.Show("Выберите группу для копирования");
                            return;
                        }
                    }

                    else
                    {
                        if (gui.StandartFloorComboBox.SelectedIndex < 0)
                        {
                            System.Windows.Forms.MessageBox.Show("Выберите типовой этаж");
                            return;
                        }

                        if (gui.FinishingCheckedListBox.CheckedItems.Count == 0)
                        {
                            System.Windows.Forms.MessageBox.Show("Выберите типы отделочных стен на типовом этаже для копирования");
                            return;
                        }
                    }

                    gui.DialogResult = DialogResult.OK;
                };

                if (gui.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                Transaction tr = new Transaction(doc, "Копирование отделки");
                tr.Start();

                GroupType finishingGroupType = null;

                if (gui.GroupsRadioButton.Checked)
                {
                    finishingGroupType = existingFinishingGroups[gui.GroupsComboBox.SelectedIndex];
                }

                else
                {
                    var selectedLevel = levels[gui.StandartFloorComboBox.SelectedIndex];
                    var levelWallTypes = gui.FinishingCheckedListBox.Tag as List<ElementId>;
                    var selectedWallTypes = new List<ElementId>();
                    foreach (int ind in gui.FinishingCheckedListBox.CheckedIndices)
                        selectedWallTypes.Add(levelWallTypes[ind]);

                    var levelWalls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WherePasses(new ElementLevelFilter(selectedLevel.Id)).Cast<Wall>()
                        .Where(x => selectedWallTypes.Contains(x.WallType.Id));

                    var conflictingElements = levelWalls.Where(x => x.GroupId != ElementId.InvalidElementId);

                    if (conflictingElements.Count() > 0)
                    {
                        TaskDialog conflictDialog = new TaskDialog("Предупреждение");
                        conflictDialog.MainContent = "Одна или несколько выбранных стен уже добавлены в другие группы проекта. Нажмите \"Показать подробнее\", чтобы посмотреть список конфликтных элементов";
                        conflictDialog.ExpandedContent = string.Join("\n", conflictingElements.Select(x => doc.GetElement(x.GroupId).Name + ": " + x.Name + " (" + x.Id.IntegerValue + ")").OrderBy(x => x));
                        conflictDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Пропустить элементы и продолжить");
                        conflictDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Разгруппировать конфликтные группы");
                        conflictDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Отменить");

                        var conflictDialogResult = conflictDialog.Show();

                        if (conflictDialogResult == TaskDialogResult.CommandLink1)
                        {
                            var newGroup = doc.Create.NewGroup(levelWalls.Where(x => x.GroupId == ElementId.InvalidElementId).Select(x => x.Id).ToList());
                            finishingGroupType = newGroup.GroupType;
                            finishingGroupType.Name = gui.UserGroupNameTextBox.Text;
                        }

                        else if (conflictDialogResult == TaskDialogResult.CommandLink2)
                        {
                            var conflictingGroups = conflictingElements.Select(x => x.GroupId).Distinct().Select(x => doc.GetElement(x));
                            foreach (Group g in conflictingGroups)
                                g.UngroupMembers();

                            doc.Regenerate();

                            var newGroup = doc.Create.NewGroup(levelWalls.Select(x => x.Id).ToList());
                            finishingGroupType = newGroup.GroupType;
                            finishingGroupType.Name = gui.UserGroupNameTextBox.Text;
                        }

                        else
                        {
                            return Result.Cancelled;
                        }
                    }

                    else
                    {
                        var newGroup = doc.Create.NewGroup(levelWalls.Select(x => x.Id).ToList());
                        finishingGroupType = newGroup.GroupType;
                        finishingGroupType.Name = gui.UserGroupNameTextBox.Text;
                    }
                }

                Group finishingGroupInstance = null;
                foreach (Group g in finishingGroupType.Groups)
                {
                    finishingGroupInstance = g;
                    break;
                }

                var groupLocation = finishingGroupInstance.Location as LocationPoint;
                XYZ groupInsertPoint = groupLocation.Point;

                List<Wall> finishingWalls = finishingGroupInstance.GetMemberIds().Select(x => doc.GetElement(x)).Cast<Wall>().ToList();
                Level finishingLevel = doc.GetElement(finishingWalls.First().LevelId) as Level;

                List<Level> copyLevels = new List<Level>();
                foreach (string selectedLevel in gui.LevelsCheckedListBox.CheckedItems)
                {
                    copyLevels.Add(levels.First(x => x.Name == selectedLevel));
                }

                foreach (Level l in copyLevels)
                {
                    double heightDifference = l.ProjectElevation - finishingLevel.ProjectElevation;
                    XYZ copyPoint = groupInsertPoint + heightDifference * XYZ.BasisZ;
                    var newFinishingGroup = doc.Create.PlaceGroup(copyPoint, finishingGroupType);
                }

                tr.Commit();
                return Result.Succeeded;
            }

            catch
            {
                return Result.Failed;
            }
        }

        private List<ElementId> GetGroupId(GroupType group)
        {
            var result = new List<ElementId>();
            foreach (Group g in group.Groups)
                result.Add(g.LevelId);
            return result;
        }

        private string GetNameById(Document document, ElementId id)
        {
            var element = document.GetElement(id);
            return element.Name;
        }

        private void RemoveFinishingFromGroups(Document document, List<ElementId> ids)
        {
            var collector = new FilteredElementCollector(document);
            var groups = collector.OfClass(typeof(Group))
                .ToElements();
            foreach (Group group in groups)
            {
                foreach (var id in ids)
                {
                    var name = group.Name;
                    var groupMembers = group.UngroupMembers();
                    //groupMembers.Remove(id);
                    if (groupMembers.Count > 0)
                        document.Create.NewGroup(groupMembers);
                }
            }
        }


    }
}
