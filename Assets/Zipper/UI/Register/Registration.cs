using System;
using UnityEngine;
using Zipper.Pool;
using Zipper.Resources.Asset;

namespace Zipper.UI.Register
{
    //DTO
    internal sealed class Registration
    {
        public Type ViewModelType { get; }
        public Type ViewType { get; }
        public string Address { get; }
        public bool OwnsViewModel { get; }

        public Action<IZObjectPoolManager, GameObject> CreatePool { get; }
        public Action<IZObjectPoolManager> DestroyPool { get; }
        public Func<IZObjectPoolManager, ZPanel> GetItem { get; }

        public PrefabAsset Prefab { get; set; }
        public bool PoolCreated { get; set; }

        public Registration(Type vmType, Type viewType, string address, bool ownsViewModel,
                            Action<IZObjectPoolManager, GameObject> createPool,
                            Action<IZObjectPoolManager> destroyPool,
                            Func<IZObjectPoolManager, ZPanel> getItem)
        {
            ViewModelType = vmType;
            ViewType = viewType;
            Address = address;
            OwnsViewModel = ownsViewModel;
            CreatePool = createPool;
            DestroyPool = destroyPool;
            GetItem = getItem;
        }
    }
}