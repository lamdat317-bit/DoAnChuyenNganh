namespace DACN.Models.Enums
{
    /// <summary>Trạng thái kiểm duyệt cụm sân của Admin trước khi hiển thị công khai.</summary>
    public enum FacilityApprovalStatus
    {
        Pending = 0,   // Chờ Admin duyệt
        Approved = 1,  // Đã duyệt - hiển thị cho khách đặt
        Rejected = 2   // Bị từ chối
    }
}
