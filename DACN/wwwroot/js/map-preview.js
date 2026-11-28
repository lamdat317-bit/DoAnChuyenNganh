// Nút "Kiểm tra trên Google Maps" ở form thêm/sửa cụm sân: mở Google Maps theo địa chỉ đang nhập
(function () {
    document.querySelectorAll('[data-map-preview]').forEach(function (btn) {
        var form = btn.closest('form');
        if (!form) return;
        function field(name) { return form.querySelector('[name="' + name + '"]'); }
        function fullAddress() {
            var addr = (field('Address') && field('Address').value || '').trim();
            var parts = addr ? [addr] : [];
            var district = (field('DistrictName') && field('DistrictName').value || '').trim();
            var citySel = field('CityId');
            var city = citySel && citySel.value && citySel.selectedIndex >= 0 ? citySel.options[citySel.selectedIndex].text.trim() : '';
            [district, city].forEach(function (p) {
                if (p && addr.toLowerCase().indexOf(p.toLowerCase()) < 0) parts.push(p);
            });
            return parts.join(', ');
        }
        btn.addEventListener('click', function (e) {
            var q = fullAddress();
            if (!q) {
                e.preventDefault();
                if (field('Address')) field('Address').focus();
                return;
            }
            btn.href = 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(q);
        });
    });
})();
