using System;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Equipment
{
    public class EquipmentStatusImage : NonsensicalMono
    {
        [SerializeField] private string _id;
        [SerializeField] private string _equipmentID;
#nullable enable
        [SerializeField] private TMP_Text? _idText;
        [SerializeField] private TMP_Text? _statusText;
#nullable disable
        [SerializeField] private Image _statusImage;
        [SerializeField] private Color _ok;
        [SerializeField] private Color _error;
        [SerializeField] private Color _mid;


        public string EquipmentID
        {
            get { return _equipmentID; }
            set { _equipmentID = value; }
        }

        private void Awake()
        {
            if (_idText != null)
            {
                EquipmentID = _idText.text;
            }

            Subscribe<string, int>("setEquipmentStatus", _id, SetEquipmentStatusImage);
        }

        private void SetEquipmentStatusImage(string id, int obj)
        {
            if (id != EquipmentID || this.gameObject.activeInHierarchy == false) return;
            if (_statusImage != null)
            {
                _statusImage.color = obj switch { 0 => _ok, 1 => _mid, 2 => _error, _ => _ok };
            }

            if (_statusText != null)
            {
                _statusText.color = obj switch { 0 => _ok, 1 => _mid, 2 => _error, _ => _ok };
            }
        }
    }
}
