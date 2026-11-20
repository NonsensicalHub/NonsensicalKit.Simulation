namespace CineShot.Compat
{
    public readonly struct CineCapabilities
    {
        public readonly bool CinemachinePresent;
        public readonly bool CinemachineV3;
        public readonly bool UrpVolumeDof;
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        public CineCapabilities(bool cm, bool v3, bool dof, int major, int minor, int patch)
        {
            CinemachinePresent = cm;
            CinemachineV3 = v3;
            UrpVolumeDof = dof;
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public string Version
        {
            get { return Major == 0 ? "未知" : Major + "." + Minor + "." + Patch; }
        }
    }
}
