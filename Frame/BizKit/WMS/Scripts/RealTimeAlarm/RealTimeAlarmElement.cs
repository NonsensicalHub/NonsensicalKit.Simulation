using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Frame.RealTimeAlarm
{
    public class RealTimeAlarmElement : ListTableElement<RealTimeAlarmInfo>, IPointerClickHandler
    {
        [SerializeField] private TMP_Text date;
        [SerializeField] private TMP_Text _info;
        [SerializeField] private GameObject _Status_OK;
        [SerializeField] private GameObject _Status_Error;
        [SerializeField] private Image _bg;
        [SerializeField] private Sprite[] _sp;

        private string _boundEpisodeId;
        private int _boundStatus;
        private bool _boundInterrupted;
        private string _boundDescription;
        private string _boundTimestamp;
        private string _boundResolution;

        public override void SetValue(RealTimeAlarmInfo elementData)
        {
            base.SetValue(elementData);
            if (elementData == null)
                return;

            if (!string.IsNullOrEmpty(elementData.EpisodeId)
                && elementData.EpisodeId == _boundEpisodeId
                && elementData.Status == _boundStatus
                && elementData.DataInterrupted == _boundInterrupted
                && elementData.AlarmDescription == _boundDescription
                && elementData.Timestamp == _boundTimestamp
                && elementData.ResolutionDateTime == _boundResolution)
                return;

            _boundEpisodeId = elementData.EpisodeId;
            _boundStatus = elementData.Status;
            _boundInterrupted = elementData.DataInterrupted;
            _boundDescription = elementData.AlarmDescription;
            _boundTimestamp = elementData.Timestamp;
            _boundResolution = elementData.ResolutionDateTime;

            date.text = elementData.Timestamp;
            bool recovered = elementData.Status == 2;
            _bg.sprite = recovered ? _sp[0] : _sp[1];
            _Status_OK.SetActive(recovered);
            _Status_Error.SetActive(!recovered);

            if (string.IsNullOrEmpty(elementData.EpisodeId))
            {
                _info.text = $"报警位置: {elementData.AlarmDevice}\n" +
                             $"报警描述: {elementData.AlarmType}\n" +
                             $"状态: {elementData.Status switch { 0 => "未处理", 1 => "处理中", 2 => "处理完成", _ => "" }} ";
                return;
            }

            string status = elementData.DataInterrupted
                ? "采集中断"
                : elementData.Status == 2 ? "已恢复" : "报警中";
            string resolvedLine = elementData.Status == 2
                ? $"解决时间: {(string.IsNullOrEmpty(elementData.ResolutionDateTime) ? "—" : elementData.ResolutionDateTime)}"
                : string.Empty;
            _info.text = $"报警设备: {elementData.AlarmDevice}\n" +
                         $"报警描述: {elementData.AlarmDescription}\n" +
                         $"状态: {status}\n" +
                         resolvedLine;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Publish("showRealTimeAlarmDetail", ElementData);
        }
    }
}
