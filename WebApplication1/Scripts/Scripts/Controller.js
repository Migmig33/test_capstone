app.controller('controller', function (service, InputRules, $scope, $timeout, $interval, $http, $sce, $window) {



    // ==========================================
    // 3. UI & TOAST NOTIFICATIONS
    // ==========================================
    $scope.toast = { toastShow: false, message: '', type: 'error' };

    $scope.showToast = function (message, type) {
        $scope.toast.message = message;
        $scope.toast.type = type;
        $scope.toastShow = true;

        $timeout(function () {
            $scope.toastShow = false;
        }, 8000);
    };

    // Runs the input rules on every visible field under this controller (see InputRules.js).
    // Each bad field shows its own message inline; this adds one toast and returns true.
    $scope.hasInvalidInputs = function (group) {
        var errors = InputRules.collect($scope, group);
        if (errors.length) $scope.showToast("Please correct the highlighted fields.", "error");
        return errors.length > 0;
    };

    //var systemRefreshTimer = setInterval(function () {
    //    $scope.$applyAsync(function () {
    //        $scope.getAllBrowseUnits();
    //    });
    //}, 10000);

    // ==========================================
    // 0. OCCUPANCY TYPE CONSTANTS
    //    1 = Single Occupant (one tenant, whole unit)
    //    2 = Household       (family/relatives, co-occupants upfront)
    //    3 = Bedspace        (unrelated renters, co-occupants added over time)
    //    Types 2 and 3 BOTH store occupants in the co_occupant table.
    // ==========================================
    $scope.OCC_SINGLE = 1;
    $scope.OCC_HOUSEHOLD = 2;
    $scope.OCC_BEDSPACE = 3;

    $scope.usesCoOccupants = function (typeId) {
        return typeId === $scope.OCC_HOUSEHOLD || typeId === $scope.OCC_BEDSPACE;
    };

    $scope.occupancyLabel = function (typeId) {
        if (typeId === $scope.OCC_SINGLE) return 'Single Occupant';
        if (typeId === $scope.OCC_HOUSEHOLD) return 'Household';
        if (typeId === $scope.OCC_BEDSPACE) return 'Bedspace';
        return '\u2014';
    };

    $scope.getOccupancyBadgeClass = function (typeId) {
        if (typeId === $scope.OCC_SINGLE) return 'bg-slate-50 text-slate-500 border-slate-200';
        if (typeId === $scope.OCC_HOUSEHOLD) return 'bg-green-50 text-green-700 border-green-100';
        if (typeId === $scope.OCC_BEDSPACE) return 'bg-blue-50 text-blue-700 border-blue-100';
        return 'bg-slate-50 text-slate-500 border-slate-200';
    };

    // ==========================================
    // 1. SUPERADMIN DASHBOARD
    // ==========================================
    $scope.initDashboard = function () {
        $scope.isLoading = true;

        service.GetAdminDashboardService().then(function (response) {
            var res = response.data;
            console.log(" Dashboard Data:", res);

            if (res && res.success === false) {
                $scope.showToast(res.message || "Unable to load dashboard data.", "error");
                return;
            }

            $scope.activeManagers = res.activeManagers || 0;
            $scope.lockedManagers = res.lockedManagers || 0;
            $scope.totalAccounts = res.totalAccounts || 0;
            $scope.securityAlerts = res.securityAlerts || 0;
            $scope.criticalAlerts = res.criticalAlerts || 0;
            $scope.warningAlerts = res.warningAlerts || 0;
            $scope.propertyManagers = res.propertyManagers || [];
            $scope.recentActivity = res.recentActivity || [];
        }, function () {
            $scope.showToast("Unable to load dashboard data.", "error");
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    // --- "Jane Cruz" -> "JC" ---
    $scope.getInitials = function (name) {
        if (!name) return '';
        var parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].charAt(0).toUpperCase();
        return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
    };

    // --- Icon container colors per audit type ---
    $scope.getAuditColor = function (type) {
        switch (type) {
            case 'login': return 'bg-blue-50 text-blue-600';
            case 'settings': return 'bg-slate-100 text-slate-600';
            case 'security': return 'bg-red-50 text-red-500';
            case 'user': return 'bg-emerald-50 text-emerald-600';
            default: return 'bg-slate-100 text-slate-500';
        }
    };

    // Line chart — green
    $scope.linecolors = [{
        backgroundColor: 'rgba(22, 163, 74, 0.12)',
        borderColor: '#16a34a',
        pointBackgroundColor: '#16a34a',
        pointBorderColor: '#ffffff',
        pointHoverBackgroundColor: '#15803d',
        pointHoverBorderColor: '#ffffff'
    }];

    // Bar chart — occupied green, vacant grey
    $scope.barcolors = [
        {
            backgroundColor: '#16a34a',
            borderColor: '#16a34a',
            hoverBackgroundColor: '#15803d'
        },
        {
            backgroundColor: '#cbd5e1',
            borderColor: '#cbd5e1',
            hoverBackgroundColor: '#94a3b8'
        }
    ];

    // ==========================================
    // GREENBOT CHAT LOGIC
    // ==========================================
    $scope.isBotTyping = false;
    $scope.initChat = function () {
        $scope.chatOpen = false;
        $scope.unread = 0;
        $scope.adminSuggestions = [
            "How many units are vacant?",
            "Show pending bookings",
            "List expiring leases"
        ];
        $scope.clearChat();
    };

    $scope.clearChat = function () {
        $scope.chatMessages = [
            {
                id: "init",
                sender: "bot",
                text: "Hi Admin! I'm GreenBot. Ask me about your units, tenants, payments, or bookings.",
                time: new Date()
            }
        ];
    };

    $scope.chatHistory = [];
    $scope.userMessage = "";

    $scope.sendMessage = function (suggestion) {
        $scope.chatMessages.push({
            sender: "user",
            text: $scope.userMessage || suggestion,
            time: new Date()
        });
        $scope.isBotTyping = true;

        var mes = $scope.userMessage || suggestion;
        var convo = JSON.stringify($scope.chatHistory);

        $scope.userMessage = "";

        service.sendMessageAIService(mes, convo)
            .then(function (response) {
                if (response.data.success) {
                    $scope.chatMessages.push({
                        sender: "bot",
                        text: response.data.reply,
                        time: new Date()
                    });
                    $scope.chatHistory = response.data.history;
                } else {
                    $scope.chatMessages.push({
                        sender: "bot",
                        text: response.data.message,
                        time: new Date()
                    });
                }
            })
            .catch(function () {
                $scope.chatMessages.push({
                    sender: "bot",
                    text: "Something went wrong. Please try again.",
                    time: new Date()
                });
            }).finally(function () {
                $scope.isBotTyping = false;
            });
    };

    $scope.sendMessageGuest = function (suggestion) {
        $scope.chatMessages.push({
            sender: "user",
            text: $scope.userMessage || suggestion,
            time: new Date()
        });
        $scope.isBotTyping = true;

        var mes = $scope.userMessage || suggestion;
        var convo = JSON.stringify($scope.chatHistory);

        $scope.userMessage = "";

        service.sendMessageAsGuestService(mes, convo)
            .then(function (response) {
                if (response.data.success) {
                    $scope.chatMessages.push({
                        sender: "bot",
                        text: response.data.reply,
                        time: new Date()
                    });
                    $scope.chatHistory = response.data.history;
                } else {
                    $scope.chatMessages.push({
                        sender: "bot",
                        text: response.data.message,
                        time: new Date()
                    });
                }
            })
            .catch(function () {
                $scope.chatMessages.push({
                    sender: "bot",
                    text: "Something went wrong. Please try again.",
                    time: new Date()
                });
            }).finally(function () {
                $scope.isBotTyping = false;
            });
    };

    // Renter
    $scope.initRenterChat = function () {
        $scope.chatOpen = false;
        $scope.unread = 0;

        $scope.renterSuggestions = [
            "Budget-friendly units",
            "Best value for money",
            "Affordable but spacious units",
            "Worth-it units under \u20b120,000",
            "Comfortable units for a low budget"
        ];

        $scope.chatMessages = [{
            id: "init",
            sender: "bot",
            text: "Hi! I'm GreenBot. Tell me your budget, unit type, or must-have amenities and I'll help you find a good match.",
            time: new Date()
        }];

        $scope.chatHistory = [];
        $scope.userMessage = "";
    };

    // ==========================================
    // 2. INITIALIZATION & DEFAULT STATE
    // ==========================================
    $scope.thisMonthhs = new Date();
    $scope.loading = false;

    // Modals & Confirmations
    $scope.editorModal = false;
    $scope.deleteUnitConfirm = false;
    $scope.deleteTenantConfirm = false;
    $scope.selectedBooking = false;
    $scope.declineTarget = null;
    $scope.confirmTarget = null;

    // Auth State
    $scope.step = 'credentials';
    $scope.resendCooldown = 0;

    // Filters & Sorting (Global)
    $scope.searchQuery = '';
    $scope.statusFilter = 'All';
    $scope.occupancyFilter = 'All';
    $scope.sortAsc = true;

    $scope.tenantSearchQuery = '';
    $scope.tenantStatusFilter = 'All';
    $scope.tenantLiveStatusFilter = 'All';
    $scope.tenantSortAsc = true;
    $scope.tenantContractFilter = 'All';
    $scope.tenantOccupancyFilter = 'All';

    // Unit Defaults
    $scope.beds = 'Studio';
    $scope.maxOccupants = 2;
    $scope.status = 'active';
    $scope.colorCode = '#1F6FEB';
    $scope.address = 'Green Residences, Taft Avenue, Manila';
    $scope.price = 15000;
    $scope.amenities = [];

    // File Arrays
    $scope.imageUrls = [];
    $scope.idFiles = [];
    $scope.co_occupants = [];

    // Chart Defaults
    $scope.linelabels = [];
    $scope.linedata = [[]];
    $scope.lineseries = ['Revenue'];
    $scope.barlabels = [];
    $scope.bardata = [[], []];
    $scope.barseries = ['Occupied', 'Vacant'];

    $scope.datasetOverride = [
        { label: 'Occupied' },
        { label: 'Vacant' }
    ];
    $scope.baroptions = {
        legend: {
            display: true,
            position: 'top'
        }
    };


    // ==========================================
    // 4. NAVIGATION
    // ==========================================
    $scope.goTo = function (page) {
        var routes = {
            'Home': '/System/Index',
            'Browse': '/System/RentersBrowse',
            'MBookings': '/System/RentersBookings',
            'Dashboard': '/System/Dashboard',
            'Units': '/System/Units',
            'Bookings': '/System/Bookings',
            'Tenants': '/System/Tenants',
            'Payments': '/System/Payments',
            'Maintenance': '/System/Maintenance',
            'Auth': '/System/Auth',
            'TenantPortal': '/System/TenantPortal',
            'AdminDashboard': '/System/AdminDashboard',
            'AdminManagers': '/System/AdminManagers',
            'AdminTables': '/System/AdminTables',
            'AdminLogs': '/System/AdminLogs',
            'TenantProfile': '/System/TenantProfile'
        };
        if (routes[page]) window.location.href = routes[page];
    };

    // ==========================================
    // 5. AUTHENTICATION
    // ==========================================
    function startCooldown() {
        $scope.resendCooldown = 30;
        var cooldownTimer = $interval(function () {
            $scope.resendCooldown--;
            if ($scope.resendCooldown <= 0) $interval.cancel(cooldownTimer);
        }, 1000);
    }

    $scope.authFunc = function () {
        if (!$scope.password) {
            return $scope.showToast("Please fill out email and password", "error");
        }
        if ($scope.hasInvalidInputs()) return;
        $scope.loading = true;
        var authData = { email: $scope.email, password: $scope.password };
        service.authService(authData).then(function (response) {
            if (response.data.success && response.data.otpRequired === false) {
                // Set only by the server, for the exempt tenant account after its password was verified.
                // The session is already signed in, so go straight to the portal.
                $scope.showToast("Welcome Back, Tenant!", "success");
                $scope.goTo('TenantPortal');
            } else if (response.data.success) {
                $scope.userRole = response.data.role;
                $scope.step = 'verify';
                $scope.otp = [];
                startCooldown();
                $scope.showToast(response.data.message, "success");
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }).finally(function () { $scope.loading = false; });
    };

    $scope.otpNext = function (index) {
        var val = $scope.otp[index];
        if (!/^\d?$/.test(val)) { $scope.otp[index] = ''; return; }
        if (val && index < 5) {
            document.getElementById('otp' + (index + 1)).focus();
        }
    };

    $scope.verifyCode = function () {
        var code = $scope.otp.join('');

        if (code.length < 6) {
            return $scope.showToast("Please enter a full 6-digit code", "error");
        }

        $scope.loading = true;

        service.verifyCodeService(code).then(function (response) {
            if (response.data.success) {
                var role = response.data.role || $scope.userRole;

                if (role === 1) {
                    $scope.showToast("Welcome Back, Admin!", "success");
                    $scope.goTo('AdminDashboard');
                }
                else if (role === 2) {
                    $scope.showToast("Welcome Back, Property Manager!", "success");
                    $scope.goTo('Dashboard');
                }
                else if (role === 3) {
                    $scope.showToast("Welcome Back, Tenant!", "success");
                    $scope.goTo('TenantPortal');
                }
                else {
                    $scope.showToast("Unknown role. Access denied.", "error");
                }
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }).finally(function () {
            $scope.loading = false;
        });
    };

    $scope.resend = function () {
        if ($scope.resendCooldown > 0) return;
        $scope.otp = [];
        $scope.authFunc();
    };

    // ==========================================
    // 6. PM DASHBOARD & DATA FETCHING
    // ==========================================
    $scope.getDataNum = function () {
        service.GetDashboardDataService().then(function (response) {
            var res = response.data;
            console.log("Dashboard C# Data:", res);

            $scope.activeUnitNum = res.activeUnitNum || [];
            $scope.pendingRequestNum = res.pendingRequestNum || [];
            $scope.pendingBookNum = res.pendingBookNum || [];
            $scope.totalPayment = res.totalPayment || 0;
            $scope.recentBookings = res.recentBookings || [];
            $scope.totalVacant = res.currentVacantUnits || 0;

            // Bedspace: open bed slots across partially-filled units
            $scope.availableJoinSlots = res.availableJoinSlots || 0;

            const monthNames = ["", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

            if (res.monthlyRevenue && res.monthlyRevenue.length > 0) {
                $scope.linelabels = res.monthlyRevenue.map(x => monthNames[x.Month]);
                $scope.linedata = [res.monthlyRevenue.map(x => x.Revenue)];
                $scope.lineseries = ['Revenue'];
            }

            if (res.monthlyOccupant && res.monthlyOccupant.length > 0) {
                $scope.barlabels = res.monthlyOccupant.map(x => monthNames[x.Month]);
                $scope.bardata = [
                    res.monthlyOccupant.map(x => x.OccupiedUnits),
                    res.monthlyOccupant.map(x => x.VacantUnits)
                ];
                $scope.barseries = ['Occupied Units', 'Vacant Units'];
            }
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    // Amenities
    $scope.amenityOptions = [];

    $scope.selectedAmenities = function () {
        if (!$scope.amenityOptions) return [];
        return $scope.amenityOptions.filter(function (a) {
            return a.selected === true;
        });
    };

    $scope.loadAllAmenities = function () {
        $http.get('/System/GetAllAmenities').then(function (response) {
            if (response.data.success) {
                $scope.amenityOptions = response.data.data.map(function (amenity) {
                    return {
                        id: amenity.id,
                        name: amenity.name,
                        selected: false
                    };
                });
            }
        });
    };

    // ==========================================
    // 7. UNIT MANAGEMENT (CRUD & Filters)
    // ==========================================
    $scope.getAllUnits = function () {
        service.GetAllUnitService().then(function (response) {
            $scope.units = response.data.data;
            $scope.totalUnits = $scope.units.length;
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.filteredUnits = function () {
        var list = $scope.units || [];

        if ($scope.searchQuery) {
            var q = $scope.searchQuery.toLowerCase();
            list = list.filter(u =>
                u.unitName.toLowerCase().includes(q) ||
                u.floor.toLowerCase().includes(q) ||
                u.beds.toLowerCase().includes(q)
            );
        }
        if ($scope.statusFilter !== "All") {
            list = list.filter(u => u.status.toLowerCase() === $scope.statusFilter.toLowerCase());
        }
        if ($scope.occupancyFilter !== "All") {
            list = list.filter(u => u.occupancy === $scope.occupancyFilter);
        }

        list = list.sort((a, b) => $scope.sortAsc ? a.unitName.localeCompare(b.unitName) : b.unitName.localeCompare(a.unitName));
        $scope.totalUnits = list.length;
        return list;
    };

    $scope.toggleSort = function () { $scope.sortAsc = !$scope.sortAsc; };
    $scope.setStatus = function (val) { $scope.statusFilter = val; };
    $scope.setOccupancy = function (val) { $scope.occupancyFilter = val; };

    $scope.triggerEditUnit = function (unit) {
        $scope.editorModal = true;
        $scope.unitName = unit.unitName;
        $scope.price = unit.price;
        $scope.beds = unit.beds;
        $scope.sqm = unit.sqm;
        $scope.floor = unit.floor;
        $scope.description = unit.description;
        $scope.videoUrl = unit.videoUrl;
        $scope.colorCode = unit.colorCode;
        $scope.status = unit.status;
        $scope.address = unit.address;
        $scope.maxOccupants = unit.maxOccupants;
        $scope.imageUrls = [];

        service.GetUnitImagesService(unit.Uid).then(function (response) {
            if (response.data.success) {
                $scope.imageUrls = response.data.data || [];
            }
        });
        $scope.selectedUnit = unit.Uid;

        if ($scope.amenityOptions) {
            $scope.amenityOptions.forEach(function (opt) {
                var hasAmenity = unit.amenities && unit.amenities.some(function (ua) { return ua.id === opt.id; });
                opt.selected = hasAmenity;
            });
        }
    };

    $scope.closeEditorModal = function () {
        $scope.editorModal = false;

        $scope.unitName = "";
        $scope.price = "";
        $scope.beds = "";
        $scope.sqm = "";
        $scope.floor = "";
        $scope.description = "";
        $scope.videoUrl = "";
        $scope.colorCode = "";
        $scope.status = "";
        $scope.address = "";
        $scope.maxOccupants = "";
        $scope.imageUrls = [];
        $scope.selectedUnit = null;

        if ($scope.amenityOptions) {
            $scope.amenityOptions.forEach(function (opt) {
                opt.selected = false;
            });
        }
    };

    $scope.saveUnit = function (id) {
        if ($scope.hasInvalidInputs()) return;

        var unitData = {
            unitName: InputRules.normalize('code', $scope.unitName), price: Number($scope.price), beds: $scope.beds,
            sqm: Number($scope.sqm), floor: InputRules.normalize('code', $scope.floor), description: InputRules.normalize('description', $scope.description),
            videoUrl: $scope.videoUrl, colorCode: $scope.colorCode, status: $scope.status,
            address: InputRules.normalize('address', $scope.address), maxOccupants: Number($scope.maxOccupants)
        };

        $scope.isLoading = true;
        var amenityIds = $scope.selectedAmenities().map(function (a) {
            return a.id || a.Id || a.amenityId;
        }).join(',');

        service.saveUnitService(unitData, $scope.imageUrls, amenityIds, id).then(function (response) {
            if (response.data.success) {
                $scope.showToast(response.data.message, "success");
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }).finally(function () {
            $scope.getAllUnits();
            $scope.closeEditorModal();
        });
    };

    $scope.triggerConfirmDeleteUnit = function (id) {
        $scope.selectedUnit = id;
        $scope.deleteUnitConfirm = true;
    };

    $scope.deleteUnit = function (id) {
        service.DeleteUnitService(id).then(function (response) {
            $scope.showToast(response.data.message, response.data.success ? "success" : "error");
        }).finally(function () {
            $scope.deleteUnitConfirm = false;
            $scope.getAllUnits();
        });
    };

    // ==========================================
    // 8. TENANT MANAGEMENT (CRUD & Filters)
    //    Bedspace-aware: types 2 and 3 both use co_occupant rows.
    // ==========================================
    $scope.getAllTenants = function () {
        service.GetAllTenantService().then(function (response) {
            $scope.tenants = response.data.data;
            $scope.totalTenants = $scope.tenants.length;
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    // ---- Unit occupancy helpers ----
    // Headcount for a unit = 1 main tenant + that tenant's co-occupants.
    $scope.getUnitOccupancy = function (unit) {
        var mainTenant = ($scope.tenants || []).find(function (t) {
            return t.unitId === unit.Uid && t.liveStatus !== 'Terminated' && !t.isTerminated;
        });

        var max = parseInt(unit.maxOccupants, 10) || 1;

        if (!mainTenant) {
            return {
                hasTenant: false,
                typeId: null,
                count: 0,
                max: max,
                slotsOpen: max,
                isEmpty: true,
                isBedspace: false,
                joinable: false
            };
        }

        var count = 1 + (mainTenant.additionalOccupantsCount || 0);
        var isBedspace = mainTenant.occupancyTypeId === $scope.OCC_BEDSPACE;

        return {
            hasTenant: true,
            typeId: mainTenant.occupancyTypeId,
            mainTenant: mainTenant,
            count: count,
            max: max,
            slotsOpen: Math.max(0, max - count),
            isEmpty: false,
            isBedspace: isBedspace,
            joinable: isBedspace && count < max
        };
    };

    // Fill info for the table badge (e.g. Bedspace 2/4)
    $scope.getTenantUnitFill = function (t) {
        if (!t.unitId || !$scope.units) return null;
        var u = $scope.units.find(function (x) { return x.Uid === t.unitId; });
        return u ? $scope.getUnitOccupancy(u) : null;
    };

    // Label for the Assigned Unit dropdown
    $scope.unitOptionLabel = function (u) {
        var occ = $scope.getUnitOccupancy(u);
        if (occ.isEmpty) return u.unitName + ' (Vacant \u00b7 max ' + occ.max + ')';
        return u.unitName;
    };

    // A NEW main tenant can only take a fully vacant unit.
    // Joining a bedspace unit is done by EDITING its main tenant
    // and adding a co-occupant there.
    $scope.shouldShowUnit = function (unit) {
        if ($scope.editingTenant && $scope.editingTenant.unitId === unit.Uid) return true;

        var hasLiveTenant = ($scope.tenants || []).some(function (t) {
            return t.unitId === unit.Uid && t.liveStatus !== 'Terminated' && !t.isTerminated;
        });
        return !hasLiveTenant;
    };

    $scope.onUnitChange = function () {
        $scope.selectedUnitInfo = null;

        if (!$scope.unitId) {
            $scope.maxCoOccupantsLimit = 0;
            return;
        }

        var selectedUnit = ($scope.units || []).find(function (u) { return u.Uid === $scope.unitId; });
        if (!selectedUnit) {
            $scope.maxCoOccupantsLimit = 0;
            return;
        }

        var max = parseInt(selectedUnit.maxOccupants, 10) || 1;

        // Capacity = 1 main tenant + N co-occupants
        $scope.maxCoOccupantsLimit = Math.max(0, max - 1);

        $scope.selectedUnitInfo = {
            max: max,
            used: 1 + (($scope.co_occupants || []).length),
            slotsOpen: Math.max(0, max - 1 - (($scope.co_occupants || []).length))
        };

        if ($scope.co_occupants && $scope.co_occupants.length > $scope.maxCoOccupantsLimit) {
            $scope.co_occupants = $scope.co_occupants.slice(0, $scope.maxCoOccupantsLimit);
            $scope.showToast("Occupants trimmed to fit this unit's capacity.", "info");
        }
    };

    // Keeps the live slot counter accurate as rows are added/removed
    $scope.refreshSlotInfo = function () {
        if (!$scope.selectedUnitInfo) return;
        var used = 1 + (($scope.co_occupants || []).length);
        $scope.selectedUnitInfo.used = used;
        $scope.selectedUnitInfo.slotsOpen = Math.max(0, $scope.selectedUnitInfo.max - used);
    };

    // ---- Add / edit triggers ----
    $scope.triggerAddTenant = function () {
        $scope.editingTenant = null;
        $scope.selectedTenant = null;
        $scope.tenantNumber = '';
        $scope.occupancyTypeId = $scope.OCC_SINGLE;
        $scope.passwordHash = '123';
        $scope.name = '';
        $scope.email = '';
        $scope.phone = '';
        $scope.unitId = '';
        $scope.address = '';
        $scope.occupation = '';
        $scope.leaseStart = null;
        $scope.leaseEnd = null;
        $scope.minLeaseEnd = null;
        $scope.maxCoOccupantsLimit = 0;
        $scope.selectedUnitInfo = null;
        $scope.co_occupants = [];
        $scope.idFiles = [];
        $scope.deletedCoOccupants = [];
        $scope.deletedDocumentIds = [];
        $scope.editorModal = true;
    };

    $scope.triggerEditTenant = function (tenant) {
        $scope.editorModal = true;
        $scope.editingTenant = tenant;
        $scope.selectedTenant = tenant.Tid;

        $scope.occupancyTypeId = tenant.occupancyTypeId;
        $scope.tenantNumber = tenant.tenantNumber;
        $scope.name = tenant.name;
        $scope.email = tenant.email;
        $scope.phone = tenant.phone;
        $scope.unitId = tenant.unitId;
        $scope.address = tenant.address;
        $scope.occupation = tenant.occupation;
        $scope.passwordHash = tenant.passwordHash;
        $scope.deletedCoOccupants = [];
        $scope.deletedDocumentIds = [];

        if (tenant.leaseStart) $scope.leaseStart = new Date(tenant.leaseStart);
        if (tenant.leaseEnd) $scope.leaseEnd = new Date(tenant.leaseEnd);

        $scope.updateMinLeaseEnd();

        $scope.co_occupants = tenant.coOccupants ? angular.copy(tenant.coOccupants) : [];

        var max = parseInt(tenant.maxOccupants, 10) || 1;
        $scope.maxCoOccupantsLimit = Math.max(0, max - 1);
        $scope.selectedUnitInfo = {
            max: max,
            used: 1 + $scope.co_occupants.length,
            slotsOpen: Math.max(0, max - 1 - $scope.co_occupants.length)
        };

        if (tenant.idFiles && tenant.idFiles.length > 0) {
            $scope.idFiles = tenant.idFiles.map(function (file) {
                return { id: file.id, url: file.url };
            });
        } else {
            $scope.idFiles = [];
        }
    };

    // ---- Co-occupants (Household + Bedspace) ----
    $scope.addCoOccupant = function () {
        if (!$scope.co_occupants) $scope.co_occupants = [];

        if ($scope.co_occupants.length >= $scope.maxCoOccupantsLimit) {
            return $scope.showToast("This unit is already at full capacity.", "error");
        }

        $scope.co_occupants.push({ name: '', phone: '', address: '' });
        $scope.refreshSlotInfo();

        setTimeout(function () {
            if (typeof lucide !== 'undefined') lucide.createIcons();
        }, 50);
    };

    $scope.removeCoOccupant = function (index) {
        var removedItem = $scope.co_occupants[index];
        if (removedItem && removedItem.id) {
            $scope.deletedCoOccupants.push(removedItem.id);
        }
        $scope.co_occupants.splice(index, 1);
        $scope.refreshSlotInfo();
    };

    // ---- Filters / sorting ----
    $scope.filteredTenants = function () {
        var list = $scope.tenants || [];

        if ($scope.tenantSearchQuery) {
            var q = $scope.tenantSearchQuery.toLowerCase();
            list = list.filter(t =>
                (t.name && t.name.toLowerCase().includes(q)) ||
                (t.email && t.email.toLowerCase().includes(q)) ||
                (t.unit && t.unit.toLowerCase().includes(q)) ||
                (t.tenantNumber && t.tenantNumber.toLowerCase().includes(q))
            );
        }
        if ($scope.tenantStatusFilter !== 'All') {
            list = list.filter(t => t.status && t.status.toLowerCase() === $scope.tenantStatusFilter.toLowerCase());
        }
        if ($scope.tenantLiveStatusFilter !== 'All') {
            list = list.filter(t => t.liveStatus === $scope.tenantLiveStatusFilter);
        }
        if ($scope.tenantContractFilter !== 'All') {
            list = list.filter(function (t) {
                return t.contractType === $scope.tenantContractFilter;
            });
        }
        if ($scope.tenantOccupancyFilter !== 'All') {
            list = list.filter(function (t) {
                return t.occupancyTypeId === $scope.tenantOccupancyFilter;
            });
        }

        list = list.slice().sort((a, b) => $scope.tenantSortAsc ? a.name.localeCompare(b.name) : b.name.localeCompare(a.name));
        $scope.totalTenants = list.length;
        return list;
    };

    $scope.toggleTenantSort = function () { $scope.tenantSortAsc = !$scope.tenantSortAsc; };
    $scope.setTenantStatus = function (val) { $scope.tenantStatusFilter = val; };
    $scope.setTenantLiveStatus = function (val) { $scope.tenantLiveStatusFilter = val; };
    $scope.setTenantContract = function (val) { $scope.tenantContractFilter = val; };
    $scope.setTenantOccupancy = function (val) { $scope.tenantOccupancyFilter = val; };

    $scope.updateMinLeaseEnd = function () {
        if ($scope.leaseStart) {
            var minDate = new Date($scope.leaseStart);
            minDate.setMonth(minDate.getMonth() + 3);
            var year = minDate.getFullYear();
            var month = String(minDate.getMonth() + 1).padStart(2, '0');
            var day = String(minDate.getDate()).padStart(2, '0');

            $scope.minLeaseEnd = year + '-' + month + '-' + day;

            if ($scope.leaseEnd && new Date($scope.leaseEnd) < minDate) {
                $scope.leaseEnd = null;
                $scope.showToast("Lease End cleared: Minimum contract is 3 months.", "info");
            }
        } else {
            $scope.minLeaseEnd = null;
        }
    };

    // ---- Badge helpers ----
    $scope.getDaysLeftClass = function (days) {
        if (days < 0) return 'bg-red-50 text-red-700 border-red-100';
        if (days <= 30) return 'bg-amber-50 text-amber-700 border-amber-100';
        return 'bg-blue-50 text-blue-700 border-blue-100';
    };

    $scope.getContractClass = function (type) {
        if (type === 'Short-term') return 'bg-purple-50 text-purple-700 border-purple-100';
        if (type === 'Mid-term') return 'bg-blue-50 text-blue-700 border-blue-100';
        if (type === 'Long-term') return 'bg-indigo-50 text-indigo-700 border-indigo-100';
        return 'bg-slate-50 text-slate-500 border-slate-200';
    };

    $scope.getStatusClass = function (status) {
        if (status === 'Active') return 'bg-emerald-100 text-emerald-700';
        if (status === 'Expiring') return 'bg-amber-100 text-amber-700';
        if (status === 'Expired') return 'bg-red-100 text-red-700';
        if (status === 'Terminated') return 'bg-slate-200 text-slate-700';
        return 'bg-slate-100 text-slate-500';
    };

    $scope.getPaymentClass = function (status) {
        if (status === 'Paid') return 'bg-emerald-50 text-emerald-700 border-emerald-100';
        if (status === 'Pending') return 'bg-amber-50 text-amber-700 border-amber-100';
        return 'bg-slate-50 text-slate-500 border-slate-200';
    };

    // ---- Save ----
    $scope.saveTenant = function (id) {
        if ($scope.savingTenant) return;
        if ($scope.readingIdFile) return $scope.showToast("Please wait for the ID image to finish loading.", "info");
        var formatToDateString = function (dateObj) {
            if (!dateObj) return null;
            var d = new Date(dateObj);
            var year = d.getFullYear();
            var month = String(d.getMonth() + 1).padStart(2, '0');
            var day = String(d.getDate()).padStart(2, '0');
            return year + '-' + month + '-' + day;
        };

        // ---- Validation ----
        if ($scope.hasInvalidInputs()) return;
        if (!$scope.occupancyTypeId) return $scope.showToast("Please select an Occupancy Type", "error");
        if (!$scope.unitId) return $scope.showToast("Please select a unit", "error");
        if (!$scope.leaseStart) return $scope.showToast("Lease Start date is required", "error");

        if ($scope.leaseStart && $scope.leaseEnd && new Date($scope.leaseEnd) <= new Date($scope.leaseStart)) {
            return $scope.showToast("Lease End date must be after the Lease Start date", "error");
        }

        // Household must declare at least one occupant.
        // Bedspace may start empty — slots fill over time.
        if ($scope.occupancyTypeId === $scope.OCC_HOUSEHOLD) {
            if (!$scope.co_occupants || $scope.co_occupants.length === 0) {
                return $scope.showToast("A Household must have at least one additional occupant.", "error");
            }
        }

        // Both Household and Bedspace validate whatever rows exist.
        if ($scope.usesCoOccupants($scope.occupancyTypeId)) {
            var rows = $scope.co_occupants || [];
            for (var i = 0; i < rows.length; i++) {
                if (!rows[i].name || !rows[i].phone) {
                    return $scope.showToast("Occupant #" + (i + 1) + " must have a name and phone number.", "error");
                }
            }
            if (rows.length > $scope.maxCoOccupantsLimit) {
                return $scope.showToast("This unit only allows " + $scope.maxCoOccupantsLimit + " additional occupant(s).", "error");
            }
        }

        var tenantData = {
            name: InputRules.normalize('name', $scope.name),
            email: InputRules.normalize('email', $scope.email),
            phone: InputRules.normalize('phone', $scope.phone) || null,
            unitId: $scope.unitId,
            leaseStart: formatToDateString($scope.leaseStart),
            leaseEnd: formatToDateString($scope.leaseEnd),
            address: InputRules.normalize('address', $scope.address) || null,
            occupation: InputRules.normalize('occupation', $scope.occupation) || null,
            passwordHash: $scope.passwordHash,
            occupancyTypeId: $scope.occupancyTypeId,
            deletedCoOccupants: $scope.deletedCoOccupants
        };

        // Single sends none; Household and Bedspace both send co-occupants.
        var occupantsToSend = $scope.usesCoOccupants($scope.occupancyTypeId)
            ? ($scope.co_occupants || []).map(function (occupant) {
                return {
                    id: occupant.id,
                    name: InputRules.normalize('name', occupant.name),
                    phone: InputRules.normalize('phone', occupant.phone),
                    address: InputRules.normalize('address', occupant.address) || null
                };
            })
            : [];

        var targetTid = id || ($scope.editingTenant ? $scope.editingTenant.Tid : null);

        $scope.savingTenant = true;
        service.saveTenantService(tenantData, occupantsToSend, $scope.idFiles, targetTid, $scope.deletedDocumentIds)
            .then(function (response) {
                if (response.data.success) {
                    $scope.showToast(response.data.message, "success");
                    $scope.editorModal = false;
                    if (typeof $scope.getAllTenants === 'function') $scope.getAllTenants();
                    if (typeof $scope.getAllUnits === 'function') $scope.getAllUnits();
                } else {
                    $scope.showToast(response.data.message || "Failed to save tenant.", "error");
                }
            }).catch(function (response) {
                var message = response.data && response.data.message;
                $scope.showToast(message || "Unable to save tenant. Please try again.", "error");
            }).finally(function () { $scope.savingTenant = false; });
    };

    $scope.triggerConfirmDeleteTenant = function (id) {
        $scope.selectedTenant = id;
        $scope.deleteTenantConfirm = true;
    };

    $scope.deleteTenant = function (id) {
        service.DeleteTenantService(id).then(function (response) {
            $scope.showToast(response.data.message, response.data.success ? "success" : "error");
        }).finally(function () {
            $scope.deleteTenantConfirm = false;
            $scope.getAllTenants();
            if (typeof $scope.getAllUnits === 'function') $scope.getAllUnits();
        });
    };

    // ==========================================
    // 9. BOOKING MANAGEMENT (CRUD & Filters)
    // ==========================================
    $scope.getAllBookings = function () {
        service.GetAllBookingsService().then(function (response) {
            if (response.data.success) {
                $scope.bookings = response.data.bookings;
                $scope.busyDates = response.data.busyDates || [];
                $scope.currentFilter = 'All';
                $scope.statusFilter = '';
                $scope.groupedBookings = {};
                $scope.upcomingBookings = [];

                var now = new Date();
                $scope.upcomingBookings = $scope.bookings.filter(function (b) {
                    var bookDate = new Date(b.datetime);
                    return (b.status === 'Pending' || b.status === 'Confirmed') && bookDate >= now;
                });

                $scope.upcomingBookings.forEach(function (b) {
                    var d = new Date(b.datetime);
                    var year = d.getFullYear();
                    var month = String(d.getMonth() + 1).padStart(2, '0');
                    var day = String(d.getDate()).padStart(2, '0');
                    var dateKey = year + '-' + month + '-' + day;

                    if (!$scope.groupedBookings[dateKey]) {
                        $scope.groupedBookings[dateKey] = [];
                    }
                    $scope.groupedBookings[dateKey].push(b);
                });

            } else {
                $scope.showToast("Failed to load bookings", "error");
            }
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    // Form & Loading States
    $scope.declineReason = "";
    $scope.declineCustom = "";
    $scope.declineLoading = false;
    $scope.confirmLoading = false;

    $scope.DECLINE_REASONS = [
        "Unit Unavailable",
        "Incomplete Details",
        "Schedule Conflict",
        "Other"
    ];

    $scope.openDetailModal = function (booking) {
        $scope.selectedBooking = booking;
        setTimeout(function () {
            if (window.lucide) { window.lucide.createIcons(); }
        }, 50);
    };

    $scope.closeModal = function () {
        $scope.selectedBooking = null;
    };

    $scope.isDateBusy = function (datetimeString) {
        if (!datetimeString || !$scope.busyDates) return false;
        var dateOnly = datetimeString.split('T')[0];
        return $scope.busyDates.indexOf(dateOnly) !== -1;
    };

    $scope.removeBusyDt = function (dateStr) {
        service.RemoveBusyDateService(dateStr).then(function (response) {
            if (response.data.success) {
                var idx = $scope.busyDates.indexOf(dateStr);
                if (idx !== -1) {
                    $scope.busyDates.splice(idx, 1);
                }
                $scope.showToast("Busy date removed successfully.", "success");
                $scope.getAllBookings();
            } else {
                $scope.showToast("Error: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred.", "error");
        });
    };

    $scope.clearBusyDt = function () {
        var isSure = confirm("Are you sure you want to clear ALL busy dates?");

        if (isSure) {
            service.RemoveBusyDateService("ALL").then(function (response) {
                if (response.data.success) {
                    $scope.busyDates = [];
                    $scope.showToast("All busy dates cleared.", "success");
                    $scope.getAllBookings();
                } else {
                    $scope.showToast("Error: " + response.data.message, "error");
                }
            }, function (error) {
                $scope.showToast("Server error occurred.", "error");
            });
        }
    };

    $scope.openDecline = function (booking) {
        $scope.closeModal();
        $scope.declineTarget = booking;
        $scope.declineReason = "";
        $scope.declineCustom = "";
        $scope.declineLoading = false;

        setTimeout(function () { if (window.lucide) { window.lucide.createIcons(); } }, 50);
    };

    $scope.closeDecline = function () {
        $scope.declineTarget = null;
    };

    $scope.setDeclineReason = function (reason) {
        $scope.declineReason = reason;
        if (reason !== 'Other') {
            $scope.declineCustom = "";
        }
    };

    $scope.submitDecline = function () {
        if ($scope.declineReason === 'Other' && $scope.hasInvalidInputs()) return;
        $scope.declineLoading = true;
        var finalReason = $scope.declineReason === 'Other' ? InputRules.normalize('description', $scope.declineCustom) : $scope.declineReason;

        service.DeclineBookingService($scope.declineTarget.id, finalReason).then(function (response) {
            if (response.data.success) {
                $scope.declineTarget.status = 'Declined';
                $scope.declineTarget.cancelReason = finalReason;
                $scope.declineLoading = false;
                $scope.closeDecline();

                $scope.showToast("Booking declined successfully.", "success");
                $scope.getAllBookings();
            } else {
                $scope.declineLoading = false;
                $scope.showToast("Error: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.declineLoading = false;
            $scope.showToast("Server error occurred.", "error");
        });
    };

    $scope.openConfirm = function (booking) {
        $scope.closeModal();
        $scope.confirmTarget = booking;
        $scope.confirmLoading = false;

        setTimeout(function () { if (window.lucide) { window.lucide.createIcons(); } }, 50);
    };

    $scope.aso = function () {
        $scope.showToast("1", "info");
    };

    $scope.closeConfirm = function () {
        $scope.confirmTarget = null;
    };

    $scope.submitConfirm = function () {
        $scope.confirmLoading = true;

        service.ConfirmBookingService($scope.confirmTarget.id).then(function (response) {
            if (response.data.success) {
                $scope.confirmTarget.status = 'Confirmed';
                $scope.confirmLoading = false;
                $scope.closeConfirm();

                $scope.showToast("Booking confirmed! Guest has been notified.", "success");
                $scope.getAllBookings();
            } else {
                $scope.confirmLoading = false;
                $scope.showToast("Error: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.confirmLoading = false;
            $scope.showToast("Server error occurred.", "error");
        });
    };

    $scope.setFilter = function (tabName) {
        $scope.currentFilter = tabName;
        if (tabName === 'All') {
            $scope.statusFilter = '';
        } else {
            $scope.statusFilter = tabName;
        }
    };

    $scope.isBusyOpen = false;
    $scope.busyDates = [];

    $scope.DAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
    $scope.MONTHS_FULL = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

    var today = new Date();
    $scope.todayDate = today.getDate();
    $scope.todayMonth = today.getMonth();
    $scope.todayYear = today.getFullYear();

    $scope.calMonth = today.getMonth();
    $scope.calYear = today.getFullYear();

    $scope.generateCalendar = function () {
        var firstDay = new Date($scope.calYear, $scope.calMonth, 1).getDay();
        var daysInMonth = new Date($scope.calYear, $scope.calMonth + 1, 0).getDate();

        $scope.emptySlots = new Array(firstDay);
        $scope.monthDays = [];

        for (var i = 1; i <= daysInMonth; i++) {
            var dStr = $scope.calYear + '-' + String($scope.calMonth + 1).padStart(2, '0') + '-' + String(i).padStart(2, '0');
            var isPast = false;

            if ($scope.calYear < $scope.todayYear) {
                isPast = true;
            } else if ($scope.calYear === $scope.todayYear && $scope.calMonth < $scope.todayMonth) {
                isPast = true;
            } else if ($scope.calYear === $scope.todayYear && $scope.calMonth === $scope.todayMonth && i < $scope.todayDate) {
                isPast = true;
            }

            $scope.monthDays.push({
                num: i,
                dateStr: dStr,
                isPast: isPast,
                isToday: ($scope.calYear === $scope.todayYear && $scope.calMonth === $scope.todayMonth && i === $scope.todayDate)
            });
        }
    };

    $scope.nextCal = function () {
        if ($scope.calMonth === 11) {
            $scope.calMonth = 0;
            $scope.calYear++;
        } else {
            $scope.calMonth++;
        }
        $scope.generateCalendar();
    };

    $scope.prevCal = function () {
        if ($scope.calMonth === 0) {
            $scope.calMonth = 11;
            $scope.calYear--;
        } else {
            $scope.calMonth--;
        }
        $scope.generateCalendar();
    };

    $scope.isBusy = function (dateStr) {
        return $scope.busyDates.indexOf(dateStr) !== -1;
    };

    $scope.toggleBusyDate = function (dayObj) {
        if (dayObj.isPast) return;

        service.ToggleBusyDateService(dayObj.dateStr).then(function (response) {
            if (response.data.success) {
                var idx = $scope.busyDates.indexOf(dayObj.dateStr);

                if (response.data.action === "added" && idx === -1) {
                    $scope.busyDates.push(dayObj.dateStr);
                }
                else if (response.data.action === "removed" && idx !== -1) {
                    $scope.busyDates.splice(idx, 1);
                }

                $scope.busyDates.sort();
                $scope.showToast("Busy schedule updated successfully.", "success");
                $scope.getAllBookings();

            } else {
                $scope.showToast("Failed to update busy schedule: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while updating the calendar.", "error");
        });
    };

    $scope.removeBusyDate = function (dateStr) {
        var idx = $scope.busyDates.indexOf(dateStr);
        if (idx !== -1) {
            $scope.busyDates.splice(idx, 1);
        }
    };

    $scope.clearAllBusy = function () {
        $scope.busyDates = [];
    };

    $scope.formatDateLabel = function (dateStr) {
        var parts = dateStr.split('-');
        if (parts.length !== 3) return dateStr;
        var y = parseInt(parts[0], 10);
        var m = parseInt(parts[1], 10) - 1;
        var d = parseInt(parts[2], 10);
        return $scope.MONTHS_FULL[m] + ' ' + d + ', ' + y;
    };

    $scope.generateCalendar();

    // ==========================================
    // 10. MAINTENANCE CRUD
    // ==========================================
    $scope.maintenanceRequests = [];
    $scope.units = [];

    $scope.maintenanceStatusFilter = 'All';
    $scope.maintenanceSearch = '';

    $scope.previewPhoto = null;
    $scope.scheduleOpen = false;
    $scope.addOpen = false;
    $scope.resolveConfirmReq = null;

    $scope.schedulingReq = null;
    $scope.schedDate = null;
    $scope.schedTime = null;
    $scope.isSubmittingSchedule = false;

    $scope.timeOptions = [
        "08:00 AM", "09:00 AM", "10:00 AM", "11:00 AM",
        "01:00 PM", "02:00 PM", "03:00 PM", "04:00 PM"
    ];

    $scope.openAddModal = function () {
        $scope.unitId = "";
        $scope.Tid = "";
        $scope.category = "Plumbing";
        $scope.priority = "Medium";
        $scope.description = "";

        $scope.isSubmitting = false;
        $scope.addOpen = true;

        setTimeout(function () {
            if (window.lucide) { window.lucide.createIcons(); }
        }, 50);
    };

    $scope.closeAddModal = function () {
        $scope.addOpen = false;
    };

    $scope.openResolveModal = function (req) {
        $scope.resolveConfirmReq = req;
    };

    $scope.closeResolveModal = function () {
        $scope.resolveConfirmReq = null;
    };

    $scope.markAsResolved = function (id) {
        $scope.isResolving = true;
        var payload = {
            id: id,
            status: "Resolved"
        };

        service.SaveMaintenanceRequestService(payload, id).then(function (response) {
            if (response.data.success) {
                $scope.isResolving = false;
                $scope.showToast("Request Resolved Successfully", "success");
                $scope.getAllMaintenance();
            } else {
                $scope.isResolving = false;
                $scope.showToast("Failed to Resolve, Please Try Again", "error");
            }
        }).finally(function () {
            $scope.resolveConfirmReq = null;
        });
    };

    $scope.submitAddRequest = function () {
        if (!$scope.unitId) {
            $scope.showToast("Please select a unit.", "error");
            return;
        }
        if ($scope.hasInvalidInputs()) return;

        $scope.isSubmitting = true;

        var payload = {
            id: null,
            Uid: $scope.unitId,
            Tid: $scope.Tid,
            category: $scope.category,
            priority: $scope.priority,
            description: InputRules.normalize('description', $scope.description)
        };

        service.SaveMaintenanceRequestService(payload, null).then(function (response) {
            if (response.data.success) {
                $scope.isSubmitting = false;
                $scope.closeAddModal();
                $scope.showToast(response.data.message, "success");
                $scope.getAllMaintenance();
            } else {
                $scope.isSubmitting = false;
                $scope.showToast("Failed to add request: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.isSubmitting = false;
            $scope.showToast("Server error occurred while adding the request.", "error");
        }).finally(function () {
            $scope.closeResolveModal();
        });
    };

    $scope.opnSchedModal = function (req) {
        $scope.schedulingReq = angular.copy(req);
        if (req.scheduledDate) {
            $scope.schedDate = new Date(req.scheduledDate);
        } else {
            $scope.schedDate = null;
        }

        $scope.schedTime = req.scheduledTime || null;
        $scope.isSubmittingSchedule = false;
        $scope.scheduleOpen = true;
    };

    $scope.closeScheduleModal = function () {
        $scope.scheduleOpen = false;
        $scope.schedulingReq = null;
    };

    $scope.submitSchedule = function () {
        if (!$scope.schedDate || !$scope.schedTime) {
            $scope.showToast("Please select both a date and a time.", "error");
            return;
        }

        var today = new Date();
        today.setHours(0, 0, 0, 0);
        if (new Date($scope.schedDate) < today) {
            $scope.showToast("The scheduled date cannot be in the past.", "error");
            return;
        }

        $scope.isSubmittingSchedule = true;

        var dateObj = new Date($scope.schedDate);
        var dateString = dateObj.toLocaleDateString('en-US', {
            month: '2-digit',
            day: '2-digit',
            year: 'numeric'
        });

        var combinedDateTimeStr = dateString + " " + $scope.schedTime;

        var payload = {
            id: $scope.schedulingReq.id,
            Uid: $scope.schedulingReq.Uid,
            Tid: $scope.schedulingReq.Tid,
            category: $scope.schedulingReq.category,
            priority: $scope.schedulingReq.priority,
            description: $scope.schedulingReq.description,
            scheduledDate: combinedDateTimeStr,
            status: "In Progress"
        };

        service.SaveMaintenanceRequestService(payload).then(function (response) {
            if (response.data.success) {
                $scope.closeScheduleModal();
                $scope.showToast("Schedule confirmed successfully.", "success");
                $scope.getAllMaintenance();
            } else {
                $scope.showToast("Error: " + response.data.message, "error");
            }
            $scope.isSubmittingSchedule = false;
        }, function (error) {
            $scope.showToast("Server error.", "error");
            $scope.isSubmittingSchedule = false;
        });
    };

    $scope.getAllMaintenance = function () {
        service.GetAllMaintenanceService().then(function (response) {
            if (response.data.success) {
                $scope.maintenanceRequests = response.data.requests || [];
                $scope.units = response.data.units || [];
                $scope.tenants = response.data.tenants || [];

                setTimeout(function () {
                    if (window.lucide) { window.lucide.createIcons(); }
                }, 50);

            } else {
                $scope.showToast("Failed to load maintenance data: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while fetching maintenance requests.", "error");
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.countByStatus = function (status) {
        if (!$scope.maintenanceRequests) return 0;
        return $scope.maintenanceRequests.filter(function (req) {
            return req.status === status;
        }).length;
    };

    $scope.filteredMaintenance = function () {
        if (!$scope.maintenanceRequests) return [];

        return $scope.maintenanceRequests.filter(function (req) {
            var matchStatus = true;
            if ($scope.maintenanceStatusFilter !== 'All') {
                matchStatus = (req.status === $scope.maintenanceStatusFilter);
            }

            var matchSearch = true;
            if ($scope.maintenanceSearch && $scope.maintenanceSearch.trim() !== '') {
                var term = $scope.maintenanceSearch.toLowerCase().trim();
                matchSearch =
                    (req.category && req.category.toLowerCase().includes(term)) ||
                    (req.description && req.description.toLowerCase().includes(term)) ||
                    (req.unitName && req.unitName.toLowerCase().includes(term)) ||
                    (req.tenantName && req.tenantName.toLowerCase().includes(term));
            }

            return matchStatus && matchSearch;
        });
    };

    $scope.setStatusFilter = function (status) {
        $scope.maintenanceStatusFilter = status;
    };

    // ==============================================
    // 11. PAYMENTS
    // ==============================================
    $scope.payments = [];
    $scope.paymentFilter = 'All';
    $scope.paymentSearch = '';

    $scope.totalPaid = 0;
    $scope.totalUnpaid = 0;

    $scope.openMarkPaidModal = function (payment) {
        $scope.markPaidTarget = payment;
        $scope.paidUntil = null;
        $scope.previewMonths = [];
        $scope.generateMonthOptions();
    };

    $scope.selectPaidUntil = function (opt) {
        if ($scope.paidUntil === opt.key) {
            $scope.paidUntil = null;
            $scope.previewMonths = [];
        } else {
            $scope.paidUntil = opt.key;
            $scope.updatePreviewMonths();
        }
    };

    $scope.isInRange = function (opt) {
        if (!$scope.paidUntil) return false;

        const optIdx = $scope.monthOptions.findIndex(m => m.key === opt.key);
        const endIdx = $scope.monthOptions.findIndex(m => m.key === $scope.paidUntil);
        return optIdx >= 0 && optIdx <= endIdx;
    };

    $scope.updatePreviewMonths = function () {
        if (!$scope.paidUntil) { $scope.previewMonths = []; return; }

        const endIdx = $scope.monthOptions.findIndex(m => m.key === $scope.paidUntil);
        $scope.previewMonths = $scope.monthOptions
            .slice(0, endIdx + 1)
            .map(m => m.shortMonth + ' ' + m.year);
    };

    $scope.generateMonthOptions = function () {
        const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        const options = [];
        let date = new Date();

        for (let i = 0; i < 12; i++) {
            let futureDate = new Date(date.getFullYear(), date.getMonth() + i, 1);
            options.push({
                shortMonth: months[futureDate.getMonth()],
                year: futureDate.getFullYear(),
                key: futureDate.getMonth() + '-' + futureDate.getFullYear()
            });
        }
        $scope.monthOptions = options;
    };

    $scope.confirmMarkPaid = function () {
        if (!$scope.paidUntil || !$scope.markPaidTarget) return;

        const endIdx = $scope.monthOptions.findIndex(m => m.key === $scope.paidUntil);
        const months = $scope.monthOptions.slice(0, endIdx + 1).map(m => ({
            Month: parseInt(m.key.split('-')[0]) + 1,
            Year: parseInt(m.key.split('-')[1])
        }));

        const payload = {
            Tid: $scope.markPaidTarget.Tid,
            Uid: $scope.markPaidTarget.Uid,
            amount: $scope.markPaidTarget.amount,
            months: months
        };

        service.MarkPaymentsPaidService(payload)
            .then(function (res) {
                if (res.data.success) {
                    $scope.showToast('Payments updated successfully!', 'success');
                    $scope.markPaidTarget = null;
                    $scope.paidUntil = null;
                    $scope.previewMonths = [];
                    $scope.getAllPayments();
                } else {
                    $scope.showToast('Error: ' + res.data.message, "error");
                }
            });
    };

    $scope.getAllPayments = function () {
        service.getAllPaymentService().then(function (response) {
            if (response.data.success) {
                $scope.payments = response.data.data;
                $scope.calculateTotals();

                setTimeout(function () {
                    if (window.lucide) { window.lucide.createIcons(); }
                }, 50);

            } else {
                $scope.showToast("Error loading payments: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while fetching payments.", "error");
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.calculateTotals = function () {
        $scope.totalPaid = 0;
        $scope.totalUnpaid = 0;

        $scope.payments.forEach(function (p) {
            if (p.status === 'Paid') $scope.totalPaid += p.amount;
            else if (p.status === 'Unpaid') $scope.totalUnpaid += p.amount;
        });
    };

    $scope.countByPaymentStatus = function (status) {
        if (!$scope.payments) return 0;
        return $scope.payments.filter(function (p) { return p.status === status; }).length;
    };

    // Renamed so it no longer overwrites the maintenance setStatusFilter.
    // If your Payments view still calls setStatusFilter(...), point it here.
    $scope.setPaymentStatusFilter = function (status) {
        $scope.paymentFilter = status;
    };

    $scope.filteredPayments = function () {
        if (!$scope.payments) return [];

        return $scope.payments.filter(function (p) {
            var matchStatus = ($scope.paymentFilter === 'All' || p.status === $scope.paymentFilter);
            var matchSearch = true;
            if ($scope.paymentSearch && $scope.paymentSearch.trim() !== '') {
                var term = $scope.paymentSearch.toLowerCase().trim();
                matchSearch = (p.tenantName && p.tenantName.toLowerCase().includes(term)) ||
                    (p.unitName && p.unitName.toLowerCase().includes(term)) ||
                    (p.month && p.month.toLowerCase().includes(term));
            }

            return matchStatus && matchSearch;
        });
    };

    // ==========================================
    // 12. FILE UPLOADS (Images & IDs)
    // ==========================================
    $scope.MAX_UNIT_IMAGES = 10;

    $scope.handleImageUpload = function (files) {
        if (!files || files.length === 0) return;

        if (!$scope.imageUrls) $scope.imageUrls = [];

        var room = $scope.MAX_UNIT_IMAGES - $scope.imageUrls.length;

        if (room <= 0) {
            $scope.$apply(function () {
                $scope.showToast("Maximum of " + $scope.MAX_UNIT_IMAGES + " images reached.", "error");
            });
            return;
        }

        var toProcess = Math.min(files.length, room);

        if (files.length > room) {
            $scope.$apply(function () {
                $scope.showToast("Only " + room + " more image(s) can be added.", "info");
            });
        }

        for (var i = 0; i < toProcess; i++) {
            (function (file) {
                if (file.type.indexOf('image/') !== 0) return;

                var reader = new FileReader();

                reader.onload = function (event) {
                    var img = new Image();

                    img.onload = function () {
                        var MAX_WIDTH = 1600;
                        var w = img.width;
                        var h = img.height;

                        if (w > MAX_WIDTH) {
                            h = Math.round(h * (MAX_WIDTH / w));
                            w = MAX_WIDTH;
                        }

                        var canvas = document.createElement('canvas');
                        canvas.width = w;
                        canvas.height = h;
                        canvas.getContext('2d').drawImage(img, 0, 0, w, h);

                        var resized = canvas.toDataURL('image/jpeg', 0.8);

                        $scope.$apply(function () {
                            $scope.imageUrls.push(resized);
                        });
                    };

                    img.src = event.target.result;
                };

                reader.readAsDataURL(file);
            })(files[i]);
        }

        var input = document.getElementById('unitImageInput');
        if (input) input.value = '';
    };

    $scope.videoUploading = false;
    $scope.videoName = '';

    $scope.triggerVideoUpload = function () {
        document.getElementById('unitVideoInput').click();
    };

    $scope.handleVideoUpload = function (files) {
        if (!files || files.length === 0) return;

        var file = files[0];

        $scope.$apply(function () {
            $scope.videoUploading = true;
            $scope.videoName = file.name;
        });

        service.UploadUnitVideoService(file).then(function (response) {
            if (response.data.success) {
                $scope.videoUrl = response.data.url;
                $scope.showToast("Video uploaded.", "success");
            } else {
                $scope.videoName = '';
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.videoName = '';
            $scope.showToast("Server error occurred while uploading the video.", "error");
        }).finally(function () {
            $scope.videoUploading = false;

            var input = document.getElementById('unitVideoInput');
            if (input) input.value = '';
        });
    };

    $scope.removeVideo = function () {
        $scope.videoUrl = '';
        $scope.videoName = '';
    };

    $scope.removeImage = function (index) {
        $scope.imageUrls.splice(index, 1);
    };

    $scope.handleIdUpload = function (files) {
        if (!files || files.length === 0) return;
        if ($scope.readingIdFile || $scope.savingTenant) return;

        var file = files[0];

        $scope.$apply(function () {
            if (!$scope.idFiles) $scope.idFiles = [];

            if ($scope.idFiles.length >= 2) {
                $scope.showToast("Maximum of 2 ID documents reached.", "error");
                return;
            }

            if (!/^image\/(jpeg|png|gif|bmp|tiff)$/.test(file.type)) {
                $scope.showToast("Please choose a JPG, PNG, GIF, BMP, or TIFF image.", "error");
                return;
            }
            if (file.size > 10 * 1024 * 1024) {
                $scope.showToast("Each ID image must be 10 MB or smaller.", "error");
                return;
            }

            $scope.readingIdFile = true;
            var reader = new FileReader();
            reader.onload = function (event) {
                $scope.$apply(function () {
                    $scope.readingIdFile = false;
                    $scope.idFiles.push({
                        fileObj: file,
                        name: file.name,
                        url: event.target.result
                    });
                });
            };
            reader.onerror = reader.onabort = function () {
                $scope.$apply(function () {
                    $scope.readingIdFile = false;
                    $scope.showToast("Unable to read the ID image. Please select it again.", "error");
                });
            };
            reader.readAsDataURL(file);
        });

        var idInput = document.getElementById('idFileInput');
        if (idInput) idInput.value = '';
    };

    $scope.deletedDocumentIds = [];

    $scope.removeIdFile = function (index) {
        var file = $scope.idFiles[index];
        if (file.id) {
            $scope.deletedDocumentIds.push(file.id);
        }
        $scope.idFiles.splice(index, 1);
    };

    // ==========================================
    // 13. TENANT PORTAL
    // ==========================================
    $scope.MAINTENANCE_CATEGORIES = [
        "Plumbing", "Electrical", "Air Conditioning", "Internet / WiFi",
        "Doors & Locks", "Appliances", "Flooring", "Painting", "Others"
    ];

    $scope.showRequestForm = false;
    $scope.reqCategory = $scope.MAINTENANCE_CATEGORIES[0];
    $scope.reqDescription = "";
    $scope.reqPhoto = null;
    $scope.reqPhotoName = "";
    $scope.submitting = false;

    $scope.init = function () {
        $http.get('/TenantPortal/GetCurrentTenantData')
            .then(function (response) {
                var data = response.data;

                if (!data.success) {
                    $window.location.href = '/System/Auth';
                    return;
                }

                if (!data.tenant) {
                    console.error("ERROR: Tenant object is completely missing from response!", data);
                    return;
                }
                $scope.currentTenant = data.tenant;

                $scope.currentTenant.additionalOccupants = data.coOccupants || [];
                $scope.currentTenant.unit = data.unitName;

                if ($scope.currentTenant.isTerminated === 1) {
                    $scope.handleLogout(true);
                    return;
                }

                $scope.loadPortalData($scope.currentTenant.Tid);
            })
            .catch(function (error) {
                console.error("Auth error", error);
                $window.location.href = '/System/Auth';
            }).finally(function () {
                $scope.isLoading = false;
            });
    };

    $scope.loadPortalData = function (tid) {
        $http.get('/TenantPortal/GetMaintenanceRequests?tid=' + tid).then(function (res) {
            var maintenance = res.data || [];
            $scope.myMaintenance = maintenance.sort(function (a, b) {
                return new Date(b.reportedDate).getTime() - new Date(a.reportedDate).getTime();
            });
        });

        $http.get('/TenantPortal/GetPayments?tid=' + tid).then(function (res) {
            var payments = res.data || [];
            $scope.myPayments = payments.sort(function (a, b) {
                return new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime();
            });

            $scope.latestPayment = $scope.myPayments[$scope.myPayments.length - 1] || null;
            $scope.paidPaymentsCount = $scope.myPayments.filter(function (p) { return p.status === 'Paid'; }).length;
            $scope.unpaidPaymentsCount = $scope.myPayments.filter(function (p) { return p.status === 'Unpaid'; }).length;
        });

        if ($scope.currentTenant.leaseEnd) {
            var t = new Date();
            t.setHours(0, 0, 0, 0);
            var end = new Date($scope.currentTenant.leaseEnd);
            end.setHours(0, 0, 0, 0);
            $scope.daysLeft = Math.round((end.getTime() - t.getTime()) / (1000 * 60 * 60 * 24));
        } else {
            $scope.daysLeft = null;
        }

        if ($scope.daysLeft === null) $scope.liveStatus = "active";
        else if ($scope.daysLeft < 0) $scope.liveStatus = "inactive";
        else if ($scope.daysLeft <= 45) $scope.liveStatus = "expiring";
        else $scope.liveStatus = "active";

        $scope.hasCoOccupants = $scope.currentTenant.additionalOccupants && $scope.currentTenant.additionalOccupants.length > 0;
    };

  
    $scope.handleLogout = function (wasTerminated) {
        $http.post('/Auth/Logout').then(function () {
            $window.location.href = '/System/Auth';
            if (wasTerminated) {
                $scope.showToast("Your account has been terminated. Please contact the property management office.", "error");
            }
        });
    };

    $scope.toggleRequestForm = function () {
        $scope.showRequestForm = !$scope.showRequestForm;
    };

    $scope.triggerPhotoUpload = function () {
        document.getElementById('photoInput').click();
    };

    $scope.handlePhotoChange = function (event) {
        var file = event.target.files[0];
        if (!file) return;

        if (!file.type.startsWith("image/")) {
            $scope.showToast("Please select an image file.", "error");
            return;
        }
        if (file.size > 5 * 1024 * 1024) {
            $scope.showToast("Image must be under 5MB.", "error");
            return;
        }

        $scope.$apply(function () {
            $scope.reqPhotoName = file.name;
        });

        var reader = new FileReader();
        reader.onload = function (ev) {
            $scope.$apply(function () {
                $scope.reqPhoto = ev.target.result;
            });
        };
        reader.readAsDataURL(file);
    };

    $scope.handleRemovePhoto = function () {
        $scope.reqPhoto = null;
        $scope.reqPhotoName = "";
        var input = document.getElementById('photoInput');
        if (input) input.value = "";
    };
    $scope.requestForm = {
        reqDescription: "",
        reqCategory: ""    
    }
    $scope.handleSubmitRequest = function () {
        if ($scope.hasInvalidInputs('maintenance')) return;
        if (!$scope.currentTenant) {
            $scope.showToast("System error: Tenant data not loaded.", "error");
            return;
        } 
        $scope.submitting = true;

        var newRequest = {
            Tid: $scope.currentTenant.Tid,
            Uid: $scope.currentTenant.unitId,
            category: $scope.requestForm.reqCategory,
            description: $scope.requestForm.reqDescription.trim(),
            status: "Pending",
            priority: "Medium",
            reportedDate: new Date().toISOString()
        };
        console.log(newRequest);
        if ($scope.reqPhoto) {
            newRequest.reqPhoto = $scope.reqPhoto;
        }

        $http.post('/TenantPortal/SubmitMaintenanceRequest', newRequest)
            .then(function (response) {
                if (response.data.success) {
                    $scope.showToast("Maintenance request submitted! We'll get back to you soon.", "success");
                    $scope.loadPortalData($scope.currentTenant.Tid);

                    $scope.reqDescription = "";
                    $scope.reqCategory = $scope.MAINTENANCE_CATEGORIES[0];
                    $scope.handleRemovePhoto();
                    $scope.showRequestForm = false;
                } else {
                    $scope.showToast(response.data.message || "Failed to submit request.", "error");
                }
            })
            .catch(function (error) {
                console.error("HTTP POST Failed:", error);
                $scope.showToast("A server error occurred.", "error");
            })
            .finally(function () {
                $scope.submitting = false;
            });
    };

    $scope.getPaymentBadgeClass = function (status) {
        if (status === "Paid") return "bg-emerald-50 text-emerald-700 border border-emerald-100";
        return "bg-amber-50 text-amber-700 border border-amber-100";
    };

    $scope.getPaymentRowClass = function (status) {
        if (status === "Paid") return "border-l-4 border-emerald-400 bg-emerald-50/40";
        return "border-l-4 border-amber-400 bg-amber-50/40";
    };

    $scope.getMaintenanceStatusClass = function (status) {
        if (status === "Resolved") return "bg-emerald-50 text-emerald-700";
        if (status === "In Progress") return "bg-green-50 text-green-700";
        return "bg-amber-50 text-amber-700";
    };

    $scope.formatScheduledDate = function (dateStr) {
        if (!dateStr) return "";
        return new Date(dateStr + "T00:00:00").toLocaleDateString("en-PH", {
            weekday: "short", month: "short", day: "numeric", year: "numeric"
        });
    };

    // ==========================================
    // 14. BROWSE UNITS (RENTERS)
    // ==========================================
    $scope.browseUnits = [];
    $scope.browseCount = 0;

    $scope.browseQuery = '';
    $scope.browseBeds = [];
    $scope.browseAmenities = [];
    $scope.browseRentTypes = [];
    $scope.browsePriceMin = '';
    $scope.browsePriceMax = '';

    $scope.browseShowFilters = false;
    $scope.bannerDismissed = false;

    $scope.BED_OPTIONS = [];
    $scope.AMENITY_OPTIONS = [];
    $scope.RENT_OPTIONS = [];

    $scope.getAllBrowseUnits = function () {
        var handoff = sessionStorage.getItem('browseSearch');
        if (handoff) {
            $scope.browseQuery = handoff;
            sessionStorage.removeItem('browseSearch');
        }

        service.GetBrowseUnitsService().then(function (response) {
            if (response.data.success) {
                $scope.browseUnits = response.data.data || [];
                $scope.BED_OPTIONS = response.data.bedOptions || [];
                $scope.AMENITY_OPTIONS = response.data.amenityOptions || [];
                $scope.RENT_OPTIONS = response.data.rentTypeOptions || [];

                $scope.browseCount = $scope.browseUnits.length;

                setTimeout(function () {
                    if (window.lucide) { window.lucide.createIcons(); }
                }, 50);

            } else {
                $scope.showToast("Error loading units: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while fetching units.", "error");
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.filteredBrowse = function () {
        var list = $scope.browseUnits || [];

        if ($scope.browseQuery) {
            var q = $scope.browseQuery.toLowerCase().trim();
            list = list.filter(u =>
                (u.name && u.name.toLowerCase().includes(q)) ||
                (u.beds && u.beds.toLowerCase().includes(q)) ||
                (u.description && u.description.toLowerCase().includes(q)) ||
                (u.address && u.address.toLowerCase().includes(q)) ||
                (u.amenities && u.amenities.some(a => a.toLowerCase().includes(q)))
            );
        }

        if ($scope.browseBeds.length > 0) {
            list = list.filter(u => $scope.browseBeds.indexOf(u.beds) !== -1);
        }

        if ($scope.browseAmenities.length > 0) {
            list = list.filter(u =>
                $scope.browseAmenities.every(a => u.amenities && u.amenities.indexOf(a) !== -1)
            );
        }
        if ($scope.browseRentTypes.length > 0) {
            list = list.filter(u => {
                var rentType = u.joinable ? "Bedspace" : "Vacant";
                return $scope.browseRentTypes.indexOf(rentType) !== -1;
            })
        }
        if ($scope.browsePriceMin) {
            list = list.filter(u => u.price >= parseInt($scope.browsePriceMin));
        }
        if ($scope.browsePriceMax) {
            list = list.filter(u => u.price <= parseInt($scope.browsePriceMax));
        }

        $scope.browseCount = list.length;
        return list;
    };

    $scope.activeFilterCount = function () {
        return $scope.browseBeds.length +
            $scope.browseAmenities.length +
            ($scope.browsePriceMin ? 1 : 0) +
            ($scope.browsePriceMax ? 1 : 0);
    };

    $scope.isBedSelected = function (bed) {
        return $scope.browseBeds.indexOf(bed) !== -1;
    };

    $scope.isAmenitySelected = function (amenity) {
        return $scope.browseAmenities.indexOf(amenity) !== -1;
    };

    $scope.toggleBed = function (bed) {
        var idx = $scope.browseBeds.indexOf(bed);
        if (idx === -1) $scope.browseBeds.push(bed);
        else $scope.browseBeds.splice(idx, 1);
    };
    $scope.toggleRent = function (rent) {
        var idx = $scope.browseRentTypes.indexOf(rent);
        if (idx === -1) $scope.browseRentTypes.push(rent);
        else $scope.browseRentTypes.splice(idx, 1);
    }

    $scope.toggleAmenity = function (amenity) {
        var idx = $scope.browseAmenities.indexOf(amenity);
        if (idx === -1) $scope.browseAmenities.push(amenity);
        else $scope.browseAmenities.splice(idx, 1);
    };

    $scope.clearQuery = function () {
        $scope.browseQuery = '';
    };

    $scope.clearAllBrowse = function () {
        $scope.browseBeds = [];
        $scope.browseAmenities = [];
        $scope.browsePriceMin = '';
        $scope.browsePriceMax = '';
        $scope.browseQuery = '';
    };

    $scope.goToUnit = function (id) {
        service.SetSelectedUnitService(id).then(function (response) {
            if (response.data.success) {
                window.location.href = '/System/RentersUnitDetails';
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while opening the unit.", "error");
        });
    };

    // ==========================================
    // 15. RENTERS HOME
    // ==========================================
    $scope.availableUnits = [];
    $scope.featuredUnits = [];
    $scope.homeTags = [];
    $scope.homeSearch = '';

    $scope.getAllHomeUnits = function () {
        $scope.isLoading = true;

        service.GetBrowseUnitsService().then(function (response) {
            if (response.data.success) {
                $scope.availableUnits = response.data.data || [];
                $scope.featuredUnits = $scope.availableUnits.slice(0, 3);
                $scope.homeTags = response.data.amenityOptions || [];

                setTimeout(function () {
                    if (window.lucide) { window.lucide.createIcons(); }
                }, 50);

            } else {
                $scope.showToast("Error loading units: " + response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while fetching units.", "error");
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    // Browse results are already filtered to available (vacant or joinable bedspace).
    // Prefer the server-provided availabilityLabel when present.
    $scope.getOccupancy = function (unitOrName) {
        if (unitOrName && unitOrName.availabilityLabel) return unitOrName.availabilityLabel;
        return 'Vacant';
    };

    $scope.handleSearch = function () {
        $scope.goToBrowse($scope.homeSearch);
    };

    $scope.browseTag = function (tag) {
        $scope.goToBrowse(tag);
    };

    $scope.goToBrowse = function (term) {
        sessionStorage.setItem('browseSearch', term || '');
        window.location.href = '/System/RentersBrowse';
    };

    // ==========================================
    // 16. UNIT DETAIL (RENTERS)
    // ==========================================
    $scope.currentUnit = null;
    $scope.unitDetailLoading = true;
    $scope.unitOccupancy = '';
    $scope.isAvailable = false;
    $scope.relatedUnits = [];

    $scope.slideIndex = 0;

    $scope.getUnitDetail = function () {
        $scope.unitDetailLoading = true;

        return service.GetUnitDetailService().then(function (response) {
            if (response.data.success) {
                $scope.currentUnit = response.data.unit;
                $scope.unitOccupancy = response.data.occupancy;
                $scope.isAvailable = response.data.isAvailable === true;
                $scope.relatedUnits = response.data.relatedUnits || [];
                $scope.slideIndex = 0;

                setTimeout(function () {
                    if (window.lucide) { window.lucide.createIcons(); }
                }, 50);

            } else {
                $scope.currentUnit = null;
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.currentUnit = null;
            $scope.showToast("Server error occurred while loading the unit.", "error");
        }).finally(function () {
            $scope.unitDetailLoading = false;
        });
    };

    $scope.setSlide = function (index) {
        $scope.slideIndex = index;
    };

    function copyTextFallback(text) {
        var input = document.createElement('textarea');
        input.value = text;
        input.setAttribute('readonly', '');
        input.style.position = 'fixed';
        input.style.opacity = '0';
        document.body.appendChild(input);
        input.select();

        var copied = false;
        try {
            copied = document.execCommand('copy') === true;
        } catch (error) {
            copied = false;
        } finally {
            document.body.removeChild(input);
        }

        return copied;
    }

    $scope.copyUnitLink = function () {
        if (!$scope.currentUnit || !$scope.currentUnit.id) {
            return $scope.showToast('Unable to copy this unit link.', 'error');
        }

        var origin = $window.location.origin ||
            ($window.location.protocol + '//' + $window.location.host);
        var link = origin + '/System/RentersUnitDetails?id=' +
            encodeURIComponent($scope.currentUnit.id);
        var copied = function () {
            $scope.showToast('Unit link copied to your clipboard.', 'success');
        };
        var fallback = function () {
            if (copyTextFallback(link)) copied();
            else $scope.showToast('Unable to copy the link. Please copy it from the address bar.', 'error');
        };

        if ($window.navigator && $window.navigator.clipboard &&
            typeof $window.navigator.clipboard.writeText === 'function') {
            try {
                $window.navigator.clipboard.writeText(link).then(copied, fallback);
            } catch (error) {
                fallback();
            }
        } else {
            fallback();
        }
    };

    $scope.backToBrowse = function () {
        window.location.href = '/System/RentersBrowse';
    };

    $scope.getOccupancyClass = function (status) {
        if (status === 'Vacant' || (status && status.indexOf('Bedspace') === 0)) return 'bg-emerald-50 text-emerald-700 border border-emerald-100';
        if (status === 'Expiring') return 'bg-amber-50 text-amber-700 border border-amber-100';
        return 'bg-slate-100 text-slate-600 border border-slate-200';
    };

    // ==========================================
    // 17. BOOKING MODAL (RENTERS / VISITOR)
    // ==========================================
    $scope.bookingOpen = false;
    $scope.bkStep = 'auth';          // auth | verify | calendar | slots | form | confirm | success
    $scope.bkVerified = false;

    $scope.bkName = '';
    $scope.bkEmail = '';
    $scope.bkPhone = '';
    $scope.bkErrors = {};
    $scope.bkLoading = false;

    $scope.bkOtp = [];
    $scope.bkOtpLoading = false;
    $scope.bkCooldown = 0;

    $scope.bkTcChecked = false;

    $scope.bkBusyDates = [];
    $scope.bkTaken = [];

    $scope.bkSelectedDate = null;
    $scope.bkSelectedSlot = null;
    $scope.bkNotes = '';
    $scope.bkLimitError = '';
    $scope.bkSubmitting = false;
    $scope.bkResult = null;
    $scope.bkBedspaceConfirmed = false;

    $scope.bkDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

    var bkToday = new Date();
    $scope.bkViewMonth = bkToday.getMonth();
    $scope.bkViewYear = bkToday.getFullYear();

    $scope.bkCheckVisitor = function () {
        service.GetVisitorSessionService().then(function (response) {
            if (response.data.verified) {
                $scope.bkVerified = true;
                $scope.bkName = response.data.name;
                $scope.bkEmail = response.data.email;
                $scope.bkPhone = response.data.phone;
            }
        });

        $scope.bkLoadAvailability();
    };

    $scope.bkLoadAvailability = function () {
        service.GetBookingAvailabilityService().then(function (response) {
            if (response.data.success) {
                $scope.bkBusyDates = response.data.busyDates || [];
                $scope.bkTaken = response.data.taken || [];
            }
            $scope.bkGenerateCalendar();
        }, function (error) {
            $scope.bkGenerateCalendar();
        });
    };

    $scope.openBooking = function () {
        $scope.bookingOpen = true;
        $scope.bkBedspaceConfirmed = false;
        $scope.bkStep = $scope.currentUnit && $scope.currentUnit.joinable
            ? 'bedspace-confirm'
            : ($scope.bkVerified ? 'calendar' : 'auth');
        $scope.bkSelectedDate = null;
        $scope.bkSelectedSlot = null;
        $scope.bkNotes = '';
        $scope.bkOtp = [];
        $scope.bkErrors = {};
        $scope.bkLimitError = '';
        $scope.bkTcChecked = false;
    };

    $scope.bkConfirmBedspace = function () {
        $scope.bkBedspaceConfirmed = true;
        $scope.bkStep = $scope.bkVerified ? 'calendar' : 'auth';
    };

    function bkStartCooldown() {
        $scope.bkCooldown = 30;
        var timer = $interval(function () {
            $scope.bkCooldown--;
            if ($scope.bkCooldown <= 0) $interval.cancel(timer);
        }, 1000);
    }

    $scope.bkSendOtp = function () {
        if ($scope.hasInvalidInputs()) return;

        $scope.bkLoading = true;

        var authData = {
            name: InputRules.normalize('name', $scope.bkName),
            email: InputRules.normalize('email', $scope.bkEmail),
            phone: InputRules.normalize('phone', $scope.bkPhone)
        };

        service.SendVisitorOtpService(authData).then(function (response) {
            if (response.data.success) {
                $scope.bkOtp = [];
                $scope.bkStep = 'verify';
                bkStartCooldown();
                $scope.showToast(response.data.message, "success");
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while sending the code.", "error");
        }).finally(function () { $scope.bkLoading = false; });
    };

    $scope.bkOtpNext = function (index) {
        var val = $scope.bkOtp[index];
        if (!/^\d?$/.test(val)) { $scope.bkOtp[index] = ''; return; }
        if (val && index < 5) {
            document.getElementById('bkOtp' + (index + 1)).focus();
        }
    };

    $scope.bkOtpBack = function (index) {
        if (event.key === 'Backspace') { 
            event.preventDefault();

            if ($scope.bkOtp[index]) {
                $scope.bkOtp[index] = '';
            } else if (index > 0) {
                $scope.bkOtp[index - 1] = '';
                document.getElementById('bkOtp' + (index - 1)).focus();
            }
        } 
    }
    
    $scope.bkVerify = function () {
        var code = $scope.bkOtp.join('');
        if (code.length < 6) {
            return $scope.showToast("Please enter the full 6-digit code.", "error");
        }

        $scope.bkOtpLoading = true;

        service.VerifyVisitorOtpService(code).then(function (response) {
            if (response.data.success) {
                $scope.bkVerified = true;
                $scope.bkName = response.data.name;
                $scope.bkEmail = response.data.email;
                $scope.bkPhone = response.data.phone;
                $scope.bkStep = 'calendar';
                $scope.showToast("Verified! Welcome, " + response.data.name, "success");
            } else {
                $scope.bkOtp = [];
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while verifying.", "error");
        }).finally(function () { $scope.bkOtpLoading = false; });
    };

    $scope.bkSwitchAccount = function () {
        service.ClearVisitorSessionService().then(function () {
            $scope.bkVerified = false;
            $scope.bkName = '';
            $scope.bkEmail = '';
            $scope.bkPhone = '';
            $scope.bkOtp = [];
            $scope.bkErrors = {};
            $scope.bkTcChecked = false;
            $scope.bkStep = 'auth';
        });
    };

    function bkPad(n) { return String(n).padStart(2, '0'); }

    $scope.bkGenerateCalendar = function () {
        var firstDay = new Date($scope.bkViewYear, $scope.bkViewMonth, 1).getDay();
        var daysInMonth = new Date($scope.bkViewYear, $scope.bkViewMonth + 1, 0).getDate();

        var t = new Date();
        t.setHours(0, 0, 0, 0);

        $scope.bkEmptySlots = new Array(firstDay);
        $scope.bkMonthDays = [];

        for (var i = 1; i <= daysInMonth; i++) {
            var dStr = $scope.bkViewYear + '-' + bkPad($scope.bkViewMonth + 1) + '-' + bkPad(i);
            var d = new Date($scope.bkViewYear, $scope.bkViewMonth, i);

            $scope.bkMonthDays.push({
                num: i,
                dateStr: dStr,
                isPast: d < t,
                isToday: d.getTime() === t.getTime(),
                isBusy: $scope.bkBusyDates.indexOf(dStr) !== -1,
                hasBooking: $scope.bkTaken.some(function (x) { return x.date === dStr; })
            });
        }
    };

    $scope.bkShiftMonth = function (delta) {
        var d = new Date($scope.bkViewYear, $scope.bkViewMonth + delta, 1);
        $scope.bkViewMonth = d.getMonth();
        $scope.bkViewYear = d.getFullYear();
        $scope.bkGenerateCalendar();
    };

    $scope.bkSelectDate = function (day) {
        if (day.isPast || day.isBusy) return;

        $scope.bkSelectedDate = day.dateStr;
        $scope.bkSelectedSlot = null;
        $scope.bkStep = 'slots';
    };

    function bkSlotHour(slot) {
        var parts = slot.trim().split(' ');
        var hour = parseInt(parts[0].split(':')[0], 10);
        if (parts[1] === 'PM' && hour !== 12) hour += 12;
        if (parts[1] === 'AM' && hour === 12) hour = 0;
        return hour;
    }

    $scope.bkIsSlotBlocked = function (slot) {
        if (!$scope.bkSelectedDate) return false;

        var slotMinutes = bkSlotHour(slot) * 60;

        return $scope.bkTaken.some(function (x) {
            if (x.date !== $scope.bkSelectedDate) return false;
            return Math.abs(slotMinutes - (x.hour * 60)) < 60;
        });
    };

    $scope.bkSelectSlot = function (slot) {
        if ($scope.bkIsSlotBlocked(slot)) return;

        $scope.bkSelectedSlot = slot;
        $scope.bkLimitError = '';
        $scope.bkStep = 'form';
    };

    $scope.bkSubmit = function () {
        if (!$scope.bkSelectedDate || !$scope.bkSelectedSlot) return;
        if ($scope.hasInvalidInputs()) return;

        $scope.bkSubmitting = true;

        var payload = {
            date: $scope.bkSelectedDate,
            slot: $scope.bkSelectedSlot,
            notes: InputRules.normalize('description', $scope.bkNotes),
            bedspaceConfirmed: $scope.bkBedspaceConfirmed
        };

        service.SubmitVisitorBookingService(payload).then(function (response) {
            if (response.data.success) {
                $scope.bkResult = response.data;
                $scope.bkStep = 'success';
                $scope.showToast(response.data.message, "success");

                $scope.bkLoadAvailability();
            } else {
                $scope.bkLimitError = response.data.message;
                $scope.bkStep = 'form';
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while submitting the booking.", "error");
        }).finally(function () { $scope.bkSubmitting = false; });
    };

    $scope.bkFormatDate = function (dateStr) {
        if (!dateStr) return '';
        var parts = dateStr.split('-');
        var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
        return $scope.bkDAYS[d.getDay()] + ', ' + $scope.MONTHS_FULL[d.getMonth()] + ' ' + d.getDate() + ', ' + d.getFullYear();
    };

    // ==========================================
    // 18. MY BOOKINGS (RENTERS / GUEST)
    // ==========================================
    $scope.mbVerified = false;
    $scope.mbBookings = [];
    $scope.mbName = '';
    $scope.mbEmail = '';

    $scope.mbFilter = 'All';
    $scope.mbSearch = '';
    $scope.STATUS_FILTERS = ['All', 'Pending', 'Confirmed', 'Declined', 'Cancelled'];

    $scope.mbShowSignIn = false;
    $scope.mbStep = 'form';
    $scope.mbSignInName = '';
    $scope.mbSignInEmail = '';
    $scope.mbSignInPhone = '';
    $scope.mbErrors = {};
    $scope.mbLoading = false;
    $scope.mbOtp = [];
    $scope.mbOtpLoading = false;
    $scope.mbCooldown = 0;

    $scope.mbCancelTarget = null;
    $scope.mbCancelReason = '';
    $scope.mbCustomReason = '';
    $scope.mbCancelLoading = false;
    $scope.CANCEL_REASONS = [
        "Found another unit",
        "Schedule conflict",
        "Budget constraints",
        "No longer looking",
        "Other"
    ];

    $scope.getMyBookings = function () {
        service.GetMyBookingsService().then(function (response) {
            if (!response.data.success) {
                return $scope.showToast(response.data.message, "error");
            }

            $scope.mbVerified = response.data.verified;
            $scope.mbBookings = response.data.data || [];
            $scope.mbName = response.data.name || '';
            $scope.mbEmail = response.data.email || '';

        }, function (error) {
            $scope.showToast("Server error occurred while loading your bookings.", "error");
        });
    };

    $scope.filteredMyBookings = function () {
        var list = $scope.mbBookings || [];

        if ($scope.mbFilter !== 'All') {
            list = list.filter(function (b) { return b.status === $scope.mbFilter; });
        }

        if ($scope.mbSearch) {
            var q = $scope.mbSearch.toLowerCase().trim();
            list = list.filter(function (b) {
                return (b.unitName && b.unitName.toLowerCase().includes(q)) ||
                    (b.name && b.name.toLowerCase().includes(q)) ||
                    (b.email && b.email.toLowerCase().includes(q));
            });
        }

        return list;
    };

    $scope.mbCountByStatus = function (status) {
        if (!$scope.mbBookings) return 0;
        if (status === 'All') return $scope.mbBookings.length;

        return $scope.mbBookings.filter(function (b) { return b.status === status; }).length;
    };

    $scope.setMyBookingFilter = function (status) {
        $scope.mbFilter = status;
    };

    $scope.mbCanCancel = function (booking) {
        return booking.status === 'Pending' || booking.status === 'Confirmed';
    };

    $scope.getBookingStatusClass = function (status) {
        if (status === 'Confirmed') return 'bg-emerald-50 text-emerald-700 border border-emerald-100';
        if (status === 'Pending') return 'bg-amber-50 text-amber-700 border border-amber-100';
        if (status === 'Declined') return 'bg-red-50 text-red-600 border border-red-100';
        return 'bg-slate-100 text-slate-600 border border-slate-200';
    };

    $scope.mbOpenSignIn = function () {
        $scope.mbShowSignIn = true;
        $scope.mbStep = 'form';
        $scope.mbErrors = {};
        $scope.mbOtp = [];
    };

    $scope.mbCloseSignIn = function () {
        $scope.mbShowSignIn = false;
    };

    $scope.mbBackToForm = function () {
        $scope.mbStep = 'form';
        $scope.mbOtp = [];
    };

    function mbStartCooldown() {
        $scope.mbCooldown = 30;
        var timer = $interval(function () {
            $scope.mbCooldown--;
            if ($scope.mbCooldown <= 0) $interval.cancel(timer);
        }, 1000);
    }

    $scope.mbSendOtp = function () {
        if ($scope.hasInvalidInputs()) return;

        $scope.mbLoading = true;

        var authData = {
            name: InputRules.normalize('name', $scope.mbSignInName),
            email: InputRules.normalize('email', $scope.mbSignInEmail),
            phone: InputRules.normalize('phone', $scope.mbSignInPhone)
        };

        service.SendVisitorOtpService(authData).then(function (response) {
            if (response.data.success) {
                $scope.mbOtp = [];
                $scope.mbStep = 'otp';
                mbStartCooldown();
                $scope.showToast(response.data.message, "success");
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while sending the code.", "error");
        }).finally(function () { $scope.mbLoading = false; });
    };

    $scope.mbOtpNext = function (index) {
        var val = $scope.mbOtp[index];
        if (!/^\d?$/.test(val)) { $scope.mbOtp[index] = ''; return; }
        if (val && index < 5) {
            document.getElementById('mbOtp' + (index + 1)).focus();
        }
    };

    $scope.mbVerify = function () {
        var code = $scope.mbOtp.join('');
        if (code.length < 6) {
            return $scope.showToast("Please enter the full 6-digit code.", "error");
        }

        $scope.mbOtpLoading = true;

        service.VerifyVisitorOtpService(code).then(function (response) {
            if (response.data.success) {
                $scope.visitorName = response.data.name || $scope.mbSignInName;
                $scope.showToast("Welcome back, " + response.data.name + "!", "success");
                $scope.mbShowSignIn = false;
                $scope.getMyBookings();
            } else {
                $scope.mbOtp = [];
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while verifying.", "error");
        }).finally(function () { $scope.mbOtpLoading = false; });
    };

    $scope.mbSignOut = function () {
        service.ClearVisitorSessionService().then(function () {
            $scope.visitorName = '';
            $scope.mbVerified = false;
            $scope.mbBookings = [];
            $scope.mbName = '';
            $scope.mbEmail = '';
            $scope.mbShowSignIn = false;
            $scope.mbStep = 'form';
            $scope.mbSignInName = '';
            $scope.mbSignInEmail = '';
            $scope.mbSignInPhone = '';
            $scope.mbOtp = [];
        });
    };

    $scope.mbOpenCancel = function (booking) {
        $scope.mbCancelTarget = booking.id;
        $scope.mbCancelReason = '';
        $scope.mbCustomReason = '';
    };

    $scope.mbCloseCancel = function () {
        $scope.mbCancelTarget = null;
        $scope.mbCancelReason = '';
        $scope.mbCustomReason = '';
    };

    $scope.mbSetCancelReason = function (reason) {
        $scope.mbCancelReason = reason;
        if (reason !== 'Other') $scope.mbCustomReason = '';
    };

    $scope.mbConfirmCancel = function () {
        if ($scope.mbCancelReason === 'Other' && $scope.hasInvalidInputs()) return;

        var finalReason = ($scope.mbCancelReason === 'Other')
            ? ($scope.mbCustomReason || '').trim()
            : $scope.mbCancelReason;

        if (!finalReason) {
            return $scope.showToast("Please select or enter a reason for cancellation.", "error");
        }

        $scope.mbCancelLoading = true;

        service.CancelMyBookingService($scope.mbCancelTarget, finalReason).then(function (response) {
            if (response.data.success) {
                $scope.showToast(response.data.message, "success");
                $scope.mbCloseCancel();
                $scope.getMyBookings();
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while cancelling.", "error");
        }).finally(function () { $scope.mbCancelLoading = false; });
    };

    $scope.mbViewUnit = function (uid) {
        $scope.goToUnit(uid);
    };

    // ==========================================
    // 19. VISITOR SESSION (RENTERS HEADER)
    // ==========================================
    $scope.visitorName = '';

    $scope.loadVisitor = function () {
        service.GetVisitorSessionService().then(function (response) {
            $scope.visitorName = response.data.verified ? response.data.name : '';
        });
    };

    $scope.logoutVisitor = function () {
        service.ClearVisitorSessionService().then(function () {
            $scope.visitorName = '';
            window.location.reload();
        });
    };

    // ==========================================
    // 20. FORGOT PASSWORD
    // ==========================================
    $scope.fpEmail = '';
    $scope.fpOtp = [];
    $scope.fpNewPw = '';
    $scope.fpConfirmPw = '';
    $scope.fpLoading = false;
    $scope.fpSaving = false;
    $scope.fpCooldown = 0;
    $scope.fpShowNew = false;
    $scope.fpShowConfirm = false;

    $scope.openForgot = function () {
        $scope.fpEmail = $scope.email || '';
        $scope.fpOtp = [];
        $scope.fpNewPw = '';
        $scope.fpConfirmPw = '';
        $scope.step = 'forgot';
    };

    $scope.backToLogin = function () {
        $scope.step = 'credentials';
        $scope.fpOtp = [];
    };

    $scope.backToForgot = function () {
        $scope.step = 'forgot';
        $scope.fpOtp = [];
    };

    function fpStartCooldown() {
        $scope.fpCooldown = 30;
        var timer = $interval(function () {
            $scope.fpCooldown--;
            if ($scope.fpCooldown <= 0) $interval.cancel(timer);
        }, 1000);
    }

    $scope.sendResetCode = function () {
        if ($scope.hasInvalidInputs()) return;

        $scope.fpLoading = true;

        service.sendResetCodeService($scope.fpEmail).then(function (response) {
            if (response.data.success) {
                $scope.fpOtp = [];
                $scope.step = 'forgot-otp';
                fpStartCooldown();
                $scope.showToast(response.data.message, "success");
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while sending the code.", "error");
        }).finally(function () { $scope.fpLoading = false; });
    };

    $scope.fpOtpNext = function (index) {
        var val = $scope.fpOtp[index];
        if (!/^\d?$/.test(val)) { $scope.fpOtp[index] = ''; return; }
        if (val && index < 5) {
            document.getElementById('fpOtp' + (index + 1)).focus();
        }
    };

    $scope.verifyResetCode = function () {
        var code = $scope.fpOtp.join('');
        if (code.length < 6) {
            return $scope.showToast("Please enter the full 6-digit code.", "error");
        }

        $scope.fpLoading = true;

        service.verifyResetCodeService(code).then(function (response) {
            if (response.data.success) {
                $scope.fpNewPw = '';
                $scope.fpConfirmPw = '';
                $scope.step = 'reset';
            } else {
                $scope.fpOtp = [];
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while verifying.", "error");
        }).finally(function () { $scope.fpLoading = false; });
    };

    $scope.pwStrength = function () {
        var pw = $scope.fpNewPw || '';

        if (!pw) return { label: '', color: 'bg-slate-200', pct: 0 };
        if (pw.length < 6) return { label: 'Too short', color: 'bg-red-400', pct: 20 };
        if (pw.length < 8) return { label: 'Weak', color: 'bg-orange-400', pct: 40 };
        if (pw.length < 12) return { label: 'Good', color: 'bg-green-500', pct: 70 };
        return { label: 'Strong', color: 'bg-green-600', pct: 100 };
    };

    $scope.pwMatches = function () {
        return $scope.fpConfirmPw && $scope.fpConfirmPw === $scope.fpNewPw;
    };

    $scope.canSavePassword = function () {
        return !$scope.fpSaving
            && $scope.fpNewPw
            && $scope.fpNewPw.length >= 8
            && $scope.fpNewPw === $scope.fpConfirmPw;
    };

    $scope.resetPassword = function () {
        if ($scope.hasInvalidInputs()) return;
        if ($scope.fpNewPw !== $scope.fpConfirmPw) {
            return $scope.showToast("Passwords do not match.", "error");
        }

        $scope.fpSaving = true;

        service.resetPasswordService($scope.fpNewPw).then(function (response) {
            if (response.data.success) {
                $scope.showToast(response.data.message, "success");

                $scope.step = 'credentials';
                $scope.email = $scope.fpEmail;
                $scope.password = '';
                $scope.fpEmail = '';
                $scope.fpNewPw = '';
                $scope.fpConfirmPw = '';
                $scope.fpOtp = [];
            } else {
                $scope.showToast(response.data.message, "error");
            }
        }, function (error) {
            $scope.showToast("Server error occurred while saving.", "error");
        }).finally(function () { $scope.fpSaving = false; });
    };
    // Normalises stored file URLs: data: URIs pass through, disk paths get a leading slash
    $scope.fileSrc = function (url) {
        if (!url) return '';
        if (url.indexOf('data:') === 0) return url;
        if (url.indexOf('http') === 0) return url;
        return url.charAt(0) === '/' ? url : '/' + url;
    };

    $scope.isPdf = function (url) {
        if (!url) return false;
        return url.toLowerCase().indexOf('.pdf') !== -1 || url.indexOf('data:application/pdf') === 0;
    };
    // ==========================================
    // 21. PROPERTY MANAGERS PAGE (SUPERADMIN)
    // ==========================================
    $scope.propertyManagers = [];
    $scope.filteredManagers = [];
    $scope.search = '';
    $scope.filterRole = 'all';

    $scope.modalOpen = false;
    $scope.modalMode = 'create';
    $scope.editTarget = {};
    $scope.form = {};
    $scope.formErrors = {};
    $scope.saving = false;
    $scope.showPassword = false;
    $scope.showConfirm = false;

    $scope.deleteOpen = false;
    $scope.deleteTarget = null;
    $scope.deletingManager = false;

    $scope.initManagers = function () {
        $scope.isLoading = true;
        service.GetManagersService().then(function (res) {
            if (res.data && res.data.success) {
                $scope.propertyManagers = res.data.data || [];
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to load manager accounts.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to load manager accounts.', 'error');
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.getStats = function () {
        var list = $scope.propertyManagers || [];
        var active = list.filter(function (m) { return m.status === 'active'; }).length;
        return { total: list.length, active: active, inactive: list.length - active };
    };

    $scope.searchFilter = function (m) {
        var q = ($scope.search || '').toLowerCase();
        var matchesText = !q ||
            (m.name && m.name.toLowerCase().indexOf(q) !== -1) ||
            (m.email && m.email.toLowerCase().indexOf(q) !== -1);
        var matchesRole = $scope.filterRole === 'all' || m.role === $scope.filterRole;
        return matchesText && matchesRole;
    };

    $scope.avatarColor = function (id) {
        var colors = ['bg-green-500', 'bg-blue-500', 'bg-purple-500', 'bg-amber-500', 'bg-rose-500', 'bg-teal-500', 'bg-indigo-500'];
        return colors[(id || 0) % colors.length];
    };

    $scope.formatDate = function (val) { return val ? val : '\u2014'; };

    $scope.openCreate = function () {
        $scope.modalMode = 'create';
        $scope.editTarget = {};
        $scope.form = { name: '', email: '', role: 2, password: '', confirmPassword: '' };
        $scope.formErrors = {};
        $scope.showPassword = false;
        $scope.showConfirm = false;
        $scope.modalOpen = true;
    };

    $scope.openEdit = function (m) {
        $scope.modalMode = 'edit';
        $scope.editTarget = m;
        var roleValue = (m.role === 'admin' || m.role === 1 || m.role === '1') ? 1 : 2;
        $scope.form = { id: m.id, name: m.name, email: m.email, role: roleValue, password: '', confirmPassword: '' };
        $scope.formErrors = {};
        $scope.showPassword = false;
        $scope.showConfirm = false;
        $scope.modalOpen = true;
    };

    // NOTE: the Managers modal and the Booking-detail modal both used
    // closeModal. This one is scoped to Managers; they never load together.
    $scope.closeManagerModal = function () {
        $scope.modalOpen = false;
        $scope.editTarget = {};
        $scope.formErrors = {};
    };

    $scope.handleBackdropClick = function ($event) {
        if ($event.target === $event.currentTarget) $scope.closeManagerModal();
    };

    $scope.getInputClass = function (err) {
        var base = 'w-full pl-10 pr-4 py-2.5 text-sm border rounded-xl bg-white focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent transition-all';
        return base + (err ? ' border-red-300' : ' border-slate-200');
    };

    $scope.validateForm = function () {
        var e = {}, f = $scope.form;
        var nameError = InputRules.validate('name', f.name, { label: 'Full name', required: true });
        if (nameError) e.name = nameError;

        var emailError = InputRules.validate('email', f.email, { label: 'Email', required: true });
        if (emailError) e.email = emailError;

        if (f.role !== 1 && f.role !== 2) e.role = 'Select a valid role.';

        // Required when creating; optional when editing (blank keeps the current password).
        var passwordError = InputRules.validate('password', f.password, {
            label: 'Password', required: $scope.modalMode === 'create', min: 8, max: 64
        });
        if (passwordError) e.password = passwordError;
        if ((f.password || f.confirmPassword) && f.password !== f.confirmPassword) {
            e.confirmPassword = 'Passwords do not match.';
        }
        $scope.formErrors = e;
        return Object.keys(e).length === 0;
    };

    $scope.handleSave = function ($event) {
        if ($event) $event.preventDefault();
        if (!$scope.validateForm()) return;

        $scope.saving = true;
        var f = $scope.form;
        var payload = { id: f.id, name: f.name.trim(), email: f.email.trim(), role: Number(f.role), password: f.password || '' };

        var call = $scope.modalMode === 'create'
            ? service.CreateManagerService(payload)
            : service.UpdateManagerService(payload);

        call.then(function (res) {
            if (res.data && res.data.success) {
                $scope.closeManagerModal();
                $scope.initManagers();
                $scope.showToast($scope.modalMode === 'create' ? 'Manager account created.' : 'Account updated.', 'success');
            } else {
                var message = (res.data && res.data.message) || 'Unable to save the account.';
                $scope.formErrors.general = message;
                $scope.showToast(message, 'error');
            }
        }, function () {
            $scope.formErrors.general = 'Unable to save the account. Please try again.';
            $scope.showToast($scope.formErrors.general, 'error');
        }).finally(function () { $scope.saving = false; });
    };

    $scope.confirmDelete = function (m) { $scope.deleteTarget = m; $scope.deleteOpen = true; };

    $scope.handleDeleteBackdropClick = function ($event) {
        if ($event.target === $event.currentTarget) { $scope.deleteOpen = false; $scope.deleteTarget = null; }
    };

    $scope.handleDelete = function () {
        if (!$scope.deleteTarget || $scope.deletingManager) return;
        $scope.deletingManager = true;
        service.DeleteManagerService({ id: $scope.deleteTarget.id }).then(function (res) {
            if (res.data && res.data.success) {
                $scope.deleteOpen = false;
                $scope.deleteTarget = null;
                $scope.initManagers();
                $scope.showToast('Manager account deleted.', 'success');
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to delete the account.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to delete the account. Please try again.', 'error');
        }).finally(function () {
            $scope.deletingManager = false;
        });
    };

    // ==========================================
    // 22. AMENITIES ADMIN
    // ==========================================
    $scope.amenities = [];
    // Keep form values on an object so ng-if's child scope does not shadow them.
    $scope.amenityForm = { name: '' };
    $scope.isLoading = true;
    $scope.addingAmenity = false;
    $scope.deletingAmenityId = null;

    $scope.initLookup = function () {
        $scope.isLoading = true;
        service.GetAllAmenitiesService().then(function (res) {
            if (res.data && res.data.success) {
                $scope.amenities = res.data.data || [];
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to load amenities.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to load amenities.', 'error');
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.addAmenity = function () {
        var value = InputRules.normalize('code', $scope.amenityForm.name);
        if ($scope.addingAmenity) return;
        if ($scope.hasInvalidInputs()) return;

        var exists = $scope.amenities.some(function (a) {
            return a.name.toLowerCase() === value.toLowerCase();
        });
        if (exists) {
            $scope.showToast('That amenity already exists.', 'info');
            return;
        }

        $scope.addingAmenity = true;
        service.AddAmenityService({ name: value }).then(function (res) {
            if (res.data && res.data.success) {
                $scope.amenities.push({ id: res.data.id, name: res.data.name });
                $scope.amenityForm.name = '';
                $scope.showToast('Amenity added.', 'success');
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to add the amenity.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to add the amenity. Please try again.', 'error');
        }).finally(function () {
            $scope.addingAmenity = false;
        });
    };

    $scope.removeItem = function (amenity) {
        if (!amenity || $scope.deletingAmenityId !== null) return;
        if (!$window.confirm('Remove "' + amenity.name + '" from the available amenities?')) return;

        $scope.deletingAmenityId = amenity.id;
        service.DeleteAmenityService({ id: amenity.id }).then(function (res) {
            if (res.data && res.data.success) {
                var i = $scope.amenities.indexOf(amenity);
                if (i !== -1) $scope.amenities.splice(i, 1);
                $scope.showToast('Amenity removed.', 'success');
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to remove the amenity.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to remove the amenity. Please try again.', 'error');
        }).finally(function () {
            $scope.deletingAmenityId = null;
        });
    };

    // ==========================================
    // 23. AUDIT LOGS
    // ==========================================
    $scope.auditLogs = [];
    $scope.filteredLogs = [];
    $scope.counts = {};
    $scope.filter = 'all';

    $scope.auditTypes = ['login', 'settings', 'security', 'user'];

    $scope.typeFilters = [
        { id: 'all', label: 'All' },
        { id: 'login', label: 'Login' },
        { id: 'settings', label: 'Settings' },
        { id: 'security', label: 'Security' },
        { id: 'user', label: 'User' }
    ];

    $scope.initAudit = function () {
        $scope.isLoading = true;
        service.GetAuditLogsService().then(function (res) {
            if (res.data && res.data.success) {
                $scope.auditLogs = res.data.data || [];
                $scope.counts = res.data.counts || {};
            } else {
                $scope.showToast((res.data && res.data.message) || 'Unable to load the audit trail.', 'error');
            }
        }, function () {
            $scope.showToast('Unable to load the audit trail.', 'error');
        }).finally(function () {
            $scope.isLoading = false;
        });
    };

    $scope.setAuditFilter = function (type) {
        $scope.filter = ($scope.filter === type && type !== 'all') ? 'all' : type;
    };

    $scope.auditFilter = function (entry) {
        var matchesType = $scope.filter === 'all' || entry.type === $scope.filter;

        var q = ($scope.searchQuery || '').toLowerCase();
        var matchesText = !q ||
            (entry.message && entry.message.toLowerCase().indexOf(q) !== -1) ||
            (entry.actor && entry.actor.toLowerCase().indexOf(q) !== -1) ||
            (entry.ip && entry.ip.toLowerCase().indexOf(q) !== -1);

        return matchesType && matchesText;
    };

    $scope.getAuditBg = function (type) {
        switch (type) {
            case 'login': return 'bg-blue-50 text-blue-600';
            case 'settings': return 'bg-slate-100 text-slate-600';
            case 'security': return 'bg-red-50 text-red-500';
            case 'user': return 'bg-emerald-50 text-emerald-600';
            default: return 'bg-slate-100 text-slate-500';
        }
    };



    //notify of failed pdc payments
    $scope.sendPaymentNotification = function (tenantId) {
        $scope.isSending = true;
        service.sendPaymentNotifcationService(tenantId).then(function (response) {
            if (response.data.success == true) {
                $scope.showToast( response.data.message || 'Notification has been sent!', "success");
            } else {
                $scope.showToast(response.data.message, 'error');
            }
        })
        .catch(function (error) {
             $scope.showToast('Failed to send payment notification.', 'error');
        }).finally(function () {
            $scope.isSending = false;
        })
    }
    $scope.exportLogs = function () {
        $scope.isExporting = true;
        service.exportLogsService()
            .then(function () {
                $scope.showToast("CSV file downloaded successfully", "success");
            })
            .catch(function (error) {
                console.error("Export failed:", error);
                $scope.showToast("Failed to export CSV file", "error");
            })
            .finally(function () {
                $scope.isExporting = false;
            });
    }

    $scope.isEditingProfile = false;
    $scope.showProfileReasonModal = false;
    $scope.profileChangeReason = "";
    $scope.isSavingProfile = false;

    $scope.startEditProfile = function () {
        $scope.originalTenantProfile = angular.copy($scope.currentTenant);
        $scope.isEditingProfile = true;
    };

    // Open the reason modal when Save Changes is clicked
    $scope.openProfileChangeReasonModal = function () {
        $scope.profileChangeReason = "";
        $scope.showProfileReasonModal = true;
    };

    // Close the modal without submitting
    $scope.closeProfileReasonModal = function () {
        if ($scope.isSavingProfile) return;

        $scope.showProfileReasonModal = false;
        $scope.profileChangeReason = "";
    };


    $scope.handleSaveProfile = function () {
        // ---- Validation ----
        if ($scope.hasInvalidInputs('profile')) return;

        if (!reason || !reason.trim()) {

            return $scope.showToast("Please enter a reason for the profile change", "error");
        }

        var data = {
            id: $scope.id,
            requestedEmail: InputRules.normalize('email', $scope.currentTenant.email),
            requestedPhone: InputRules.normalize('phone', $scope.currentTenant.phone),
            requestedOccupation: InputRules.normalize('occupation', $scope.currentTenant.occupation),
            requestedAddress: InputRules.normalize('address', $scope.currentTenant.address),
            reason: reason.trim(),
            status: $scope.status || "pending",
            reviewNotes: $scope.reviewNotes || null
        };

       

        // Pass data to the service (see service example below)
        service.saveTenantProfileRequestService(data)
            .then(function (response) {
                if (response.data.success) {
                    $scope.showToast(response.data.message, "success");

                    // Close only after successful submission
                    $scope.showProfileReasonModal = false;
                    $scope.isEditingProfile = false;
                    $scope.profileChangeReason = "";
                    $scope.originalTenantProfile = null;
                } else {
                    $scope.showToast(response.data.message, "error");
                }
            })
            .catch(function (error) {
                $scope.showToast(
                    "There is a problem saving your request right now",
                    "error"
                );
            })
            .finally(function () {
                $scope.isSavingProfile = false;
            });
    };

    $scope.cancelEditProfile = function () {
        if ($scope.originalTenantProfile) {
            $scope.currentTenant = angular.copy($scope.originalTenantProfile);
        }

        $scope.isEditingProfile = false;
        $scope.showProfileReasonModal = false;
        $scope.profileChangeReason = "";
        $scope.originalTenantProfile = null;
    };

    $scope.showTenantProfileRequestsModal = false;
    $scope.isLoadingTenantProfileRequests = false;
    $scope.tenantProfileRequestsError = null;
    $scope.selectedProfileRequestTenantId = null;
    $scope.tenant_profile_request = [];

    $scope.openTenantProfileRequestsModal = function (tenantId) {
        $scope.selectedProfileRequestTenantId = tenantId;
        $scope.showTenantProfileRequestsModal = true;

        $scope.getTenantProfileRequestChange();
    };

    $scope.closeTenantProfileRequestsModal = function () {
        $scope.showTenantProfileRequestsModal = false;
    };

    $scope.filteredTenantProfileRequests = function () {
        var requests = $scope.tenant_profile_request || [];
        var tenantId = $scope.selectedProfileRequestTenantId;

        if (tenantId === null || tenantId === undefined) {
            return requests;
        }

        return requests.filter(function (request) {
            return String(request.Tid) === String(tenantId);
        });
    };

    $scope.getTenantProfileRequestChange = function () {
        $scope.isLoadingTenantProfileRequests = true;
        $scope.tenantProfileRequestsError = null;

        return service.getTenantProfileRequestChangeService()
            .then(function (response) {
                if (response.data && response.data.success) {
                    $scope.tenant_profile_request =
                        response.data.profile_requests || [];
                } else {
                    $scope.tenantProfileRequestsError =
                        (response.data && response.data.message) ||
                        'Unable to retrieve profile change requests.';

                    $scope.showToast(
                        $scope.tenantProfileRequestsError,
                        "error"
                    );
                }
            })
            .catch(function (error) {
                console.error(
                    "Error retrieving tenant profile change requests:",
                    error
                );

                $scope.tenantProfileRequestsError =
                    "There is a problem retrieving requests right now.";

                $scope.showToast(
                    $scope.tenantProfileRequestsError,
                    "error"
                );
            })
            .finally(function () {
                $scope.isLoadingTenantProfileRequests = false;
            });
    };


    $scope.getTenantCurrentValue = function (tenantId, field) {
        // Change $scope.tenants to the array used by your tenant table.
        var tenants = $scope.tenants || [];

        var tenant = tenants.find(function (t) {
            return String(t.Tid) === String(tenantId);
        });

        if (!tenant) return '';

        var fields = {
            email: ['email', 'Email', 'emailAddress'],
            phone: ['phone', 'Phone', 'phoneNumber'],
            occupation: ['occupation', 'Occupation'],
            address: ['address', 'Address']
        };

        var names = fields[field] || [];

        for (var i = 0; i < names.length; i++) {
            var value = tenant[names[i]];

            if (value !== undefined && value !== null && value !== '') {
                return value;
            }
        }

        return '';
    };

    $scope.updateStatusTenantProfileRequest = function (id, status, Tid, reviewNotes) {
        alert(id + status + Tid + reviewNotes)
        if (status == null) {
            return $scope.showToast("Invalid status request", "error");
        }
        var reviewError = InputRules.validate('description', reviewNotes, { label: 'Review note', required: true, max: 1000 });
        if (reviewError) {
            return $scope.showToast(reviewError, "error");
        }
        var data = {
            id: id,
            Tid: Tid,
            status: status,
            reviewNotes: reviewNotes || status
        }
        service.updateStatusTenantProfileRequestService(data).then(function (response) {
            if (response.data.success) {
                $scope.showToast(response.data.message, "success");
                $scope.showTenantProfileRequestsModal = false;
                $scope.getTenantProfileRequestChange();
            } else {
                $scope.showToast(response.data.message, "error")
            }
        }).catch(function (error) {
            $scope.showToast("There is a problem on updating the request right now.", "error");
        }).finally(function (){
            $scope.isReviewingProfileRequest = false;
        })
    }


});
