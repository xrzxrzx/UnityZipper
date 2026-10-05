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
    }
}