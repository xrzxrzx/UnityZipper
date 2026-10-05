using UnityEngine;
using Zipper.UI.Contract;

namespace Zipper.UI
{
    public sealed class ZPanelRoot : MonoBehaviour
    {
        [SerializeField] Transform _background, _main, _popup, _toast, _loading;

        public Transform GetLayer(ZPanelLayer layer) => layer switch
        {
            ZPanelLayer.Background => _background,
            ZPanelLayer.Main => _main,
            ZPanelLayer.Popup => _popup,
            ZPanelLayer.Toast => _toast,
            ZPanelLayer.Loading => _loading,
            _ => throw new System.ArgumentOutOfRangeException(nameof(layer), layer, null)
        };

        /// <summary>
        /// 运行时创建层级根 + 5 个层级父节点（**不需要场景预置**）。
        ///
        /// 为什么要有这条路：本类的 5 个字段是"对子物体的引用"——放在 prefab 资产里没问题 ✓，
        /// 但**指向本类的引用**（比如 GameLifetimeScope 上的那个字段）必须由【场景】提供 ✗ ——
        /// 而 "prefab 实例的 Inspector 里存不下场景引用"（会被 Unity 剥离 ✗）会反复制造
        /// "SetParent 到 Prefab 资产内的 Transform" 这种报错 ✓
        /// → 组装层在本类没被配置时调用它，从而**彻底不需要任何场景引用** ✓
        /// </summary>
        public static ZPanelRoot CreateRuntime(Transform parent = null)
        {
            var go = new GameObject(nameof(ZPanelRoot));
            if (parent != null)
                go.transform.SetParent(parent, false);

            var root = go.AddComponent<ZPanelRoot>();
            root._background = CreateLayer(go.transform, "Background");
            root._main       = CreateLayer(go.transform, "Main");
            root._popup      = CreateLayer(go.transform, "Popup");
            root._toast      = CreateLayer(go.transform, "Toast");
            root._loading    = CreateLayer(go.transform, "Loading");
            return root;
        }

        static Transform CreateLayer(Transform parent, string layerName)
        {
            var t = new GameObject(layerName).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}