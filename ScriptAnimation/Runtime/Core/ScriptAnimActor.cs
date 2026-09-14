using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 脚本动画对象公共基类（Timeline ScriptAnimTrack 绑定类型）。
    /// 路径移动见 <see cref="PathMoveActor"/>；叉车见 <see cref="ForkliftAnim"/>；潜伏车见 <see cref="LatentAgvAnim"/>；世界旋转锁定见 <see cref="WorldRotationLockAnim"/>；
    /// CTU 见 <see cref="CtuAnim"/>；穿梭车见 <see cref="ShuttleAnim"/>；堆垛机见 <see cref="StackerAnim"/>；
    /// 六轴机械臂见 <see cref="RobotArmAnim"/>；五轴机械臂见 <see cref="RobotArm5Anim"/>；透明度见 <see cref="FadeAnim"/>；间隔显隐见 <see cref="BlinkAnim"/>；
    /// 摇摆翻转见 <see cref="SwingFlipAnim"/>；依次换位见 <see cref="SequentialPositionAnim"/>；
    /// 姿态改变见 <see cref="PoseChangeAnim"/>、<see cref="PoseChangeAnimMax"/>；开箱见 <see cref="OpenBoxAnim"/>；缠膜见 <see cref="FilmWrapAnim"/>；
    /// 对象切换见 <see cref="ObjectSwitchAnim"/>；
    /// 任意绑定类型均可添加 <see cref="CommentClip"/> 作 Timeline 注释。
    /// </summary>
    public abstract class ScriptAnimActor : MonoBehaviour
    {
        public Transform MoverTransform => transform;
        public Transform Body => transform;
    }
}
