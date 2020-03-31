using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PlasterCopying
{
    public partial class Dialog : System.Windows.Forms.Form
    {
        public Dialog()
        {
            InitializeComponent();

        }

        private void GroupsRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (GroupsRadioButton.Checked)
                GroupsComboBox.Enabled = true;

            else
            {
                GroupsComboBox.SelectedIndex = -1;
                GroupsComboBox.Enabled = false;
            }
        }

        private void StandartFloorRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (StandartFloorRadioButton.Checked)
            {
                StandartFloorComboBox.Enabled = true;
                FinishingCheckedListBox.Enabled = true;
            }

            else
            {
                StandartFloorComboBox.Enabled = false;
                FinishingCheckedListBox.Enabled = false;
                StandartFloorComboBox.SelectedIndex = -1;
                FinishingCheckedListBox.Items.Clear();
            }
        }
    }
}
