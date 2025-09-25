namespace APIMonitorWorkerService
{
    public enum FileType
    {
        Json = 0,
        Image = 1,
        Other = 2
    }

    public enum FileStatus
    {
        Pending = 0,
        Processing = 1,
        Uploading = 2,
        Completed = 3,
        Failed = 4,
        Archived = 5
    }
}
