using System.Collections.Generic;
using NonsensicalKit.UGUI.Table;

namespace NonsensicalKit.Simulation.InteractQueueSystem
{
    public class InteractMenu : ListTableManager<InteractMenuElement, InteractMenuInfo>
    {
        protected override void Awake()
        {
            base.Awake();
            Subscribe<List<string>, int>("updateInteractMenu", OnUpdateMenu);
        }

        private void OnUpdateMenu(List<string> menuNames, int selectIndex)
        {
            if (selectIndex < 0)
            {
                CloseSelf();
            }
            else
            {
                OpenSelf();
                ElementData.Clear();
                for (int i = 0; i < menuNames.Count; i++)
                {
                    ElementData.Add(new InteractMenuInfo(i, menuNames[i], i == selectIndex));
                }

                UpdateUI();
            }
        }
    }

    public class InteractMenuInfo
    {
        public int Index;
        public string MenuName;
        public bool Selected;

        public InteractMenuInfo(int index, string menuName, bool selected)
        {
            this.Index = index;
            MenuName = menuName;
            Selected = selected;
        }
    }
}
