using System.Collections.Generic;
using Zipper.Pool;

namespace Zipper.UI
{
    public class ZPanelsState
    {
        public int StackDepth { get; private set; }          
        public int[] LayerCounts { get; private set; }             
        public List<ZPanelState> PanelStates { get; private set; }   

        internal ZPanelsState(int stackDepth, int[] layerCounts, List<ZPanelState> panelStates)
        {
            StackDepth = stackDepth;
            LayerCounts = layerCounts;
            PanelStates = panelStates;
        }
    }
}