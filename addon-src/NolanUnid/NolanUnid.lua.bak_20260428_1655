-- NolanUnid Client v54.1 - Cache + Inspect support
local ADDON_NAME = "NolanUnid"
local VERSION = 541

NolanUnidClient = NolanUnidClient or {}
NolanUnidClient.State = { lastTooltipAffixes = nil, lastGrade = 0, totalGS = 0 }
NolanUnidClient.EnchantStats = { bonusIlvl = 0, stats = {} }
-- Client-side enchant cache: maps itemLink -> {grade=, affixes={}}
NolanUnidClient.ItemCache = {}

-- Grade colors: 1=uncommon(green), 2=rare(blue), 3=epic(purple), 4=legendary(orange), 5=artifact(heirloom gold)
local GRADE_COLORS = {
    [1] = "|cff1eff00", [2] = "|cff0070dd", [3] = "|cffa335ee",
    [4] = "|cffff8000", [5] = "|cffe6cc80",
}
-- Quality -> numeric tier for comparison: green=2, blue=3, purple=4, orange=5, heirloom=7
local QUALITY_TIER = {
    [0] = 0, [1] = 1, [2] = 2, [3] = 3, [4] = 4, [5] = 5, [6] = 6, [7] = 7,
}
-- Map tier to color code
local TIER_COLORS = {
    [0] = "|cff9d9d9d", [1] = "|cffffffff", [2] = "|cff1eff00", [3] = "|cff0070dd",
    [4] = "|cffa335ee", [5] = "|cffff8000", [6] = "|cffe6cc80", [7] = "|cffe6cc80",
}
local GRADE_PREFIXES = {
    [1] = "", [2] = "优秀·", [3] = "稀有·", [4] = "传说·", [5] = "神级·",
}

-- Get display color: never lower than item's original quality
local function GetDisplayColor(grade, itemLink)
    local gradeTier = grade or 0
    local qualityTier = 0
    if itemLink then
        local _, _, q = GetItemInfo(itemLink)
        qualityTier = QUALITY_TIER[q] or 0
    end
    local effectiveTier = math.max(gradeTier, qualityTier)
    return TIER_COLORS[effectiveTier] or "|cffffffff"
end

local GS_INVTYPE_WEIGHTS = {
    [1]  = 0.70, [2]  = 0.60, [3]  = 1.00, [5]  = 1.50,
    [6]  = 1.00, [7]  = 1.50, [8]  = 1.00, [9]  = 0.70,
    [10] = 1.00, [11] = 0.70, [12] = 1.30, [13] = 1.10,
    [14] = 1.10, [15] = 0.80, [16] = 0.60, [17] = 2.30,
    [20] = 1.50, [21] = 1.10, [22] = 1.00, [23] = 1.00,
    [25] = 0.80, [26] = 0.80, [28] = 0.80,
}
local GS_TIER_MUL = { [0]=1.00, [1]=1.00, [2]=1.08, [3]=1.20, [4]=1.40, [5]=1.70 }

local function CalcClientGS(itemLink)
    if not itemLink then return 0 end
    local itemName, itemLink2, quality, ilvl = GetItemInfo(itemLink)
    if not ilvl or ilvl <= 0 then return 0 end
    local _, _, _, _, _, _, _, _, equipLoc = GetItemInfo(itemLink)
    local weight = 1.0
    if equipLoc then
        local locMap = {
            INVTYPE_HEAD=0.70, INVTYPE_NECK=0.60, INVTYPE_SHOULDER=1.00,
            INVTYPE_BODY=0, INVTYPE_CHEST=1.50, INVTYPE_WAIST=1.00,
            INVTYPE_LEGS=1.50, INVTYPE_FEET=1.00, INVTYPE_WRIST=0.70,
            INVTYPE_HAND=1.00, INVTYPE_FINGER=0.70, INVTYPE_TRINKET=1.30,
            INVTYPE_WEAPON=1.10, INVTYPE_SHIELD=1.10, INVTYPE_RANGED=0.80,
            INVTYPE_CLOAK=0.60, INVTYPE_2HWEAPON=2.30, INVTYPE_ROBE=1.50,
            INVTYPE_WEAPONMAINHAND=1.10, INVTYPE_WEAPONOFFHAND=1.00,
            INVTYPE_HOLDABLE=1.00, INVTYPE_THROWN=0.80,
            INVTYPE_RANGEDRIGHT=0.80, INVTYPE_RELIC=0.80,
        }
        weight = locMap[equipLoc] or 1.0
    end
    -- Use cached grade for this item if available
    local cached = NolanUnidClient.ItemCache[itemLink]
    local grade = cached and cached.grade or 0
    local mul = GS_TIER_MUL[grade] or 1.0
    return math.floor(ilvl * weight * mul + 0.5)
end

local function ParseServerAffixes(str)
    if not str or str == "" then return {} end
    local result = {}
    for pair in string.gmatch(str, "[^,]+") do
        local name, val = string.match(pair, "(.+):(%d+)")
        if name and val then
            table.insert(result, { affix_name = name, rolled_value = tonumber(val) or 0 })
        end
    end
    return result
end

-- ============ AIO Event Handler ============
local eventFrame = CreateFrame("Frame")
eventFrame:RegisterEvent("PLAYER_LOGIN")
eventFrame:SetScript("OnEvent", function(self, event)
    if event ~= "PLAYER_LOGIN" then return end
    self:UnregisterEvent("PLAYER_LOGIN")
    if not AIO or not AIO.RegisterEvent then return end

    AIO.RegisterEvent("NolanUnid", function(player, key, data)
        if key == "TooltipAffix" then
            -- This is for current player's bags/equips (has grade|affixes)
            local gradeStr, affStr = strsplit("|", data or "", 2)
            NolanUnidClient.State.lastGrade = tonumber(gradeStr) or 0
            NolanUnidClient.State.lastTooltipAffixes = ParseServerAffixes(affStr or "")
            
            -- Cache by current tooltip item
            if GameTooltip and GameTooltip:IsShown() then
                local _, itemLink = GameTooltip:GetItem()
                if itemLink and #NolanUnidClient.State.lastTooltipAffixes > 0 then
                    NolanUnidClient.ItemCache[itemLink] = {
                        grade = NolanUnidClient.State.lastGrade,
                        affixes = NolanUnidClient.State.lastTooltipAffixes
                    }
                end
            end
            if GameTooltip and GameTooltip:IsShown() then GameTooltip:Show() end

        elseif key == "InspectAffix" then
            -- Inspect response: grade|affixes|slot
            local gradeStr, affStr, slotStr = strsplit("|", data or "", 3)
            local grade = tonumber(gradeStr) or 0
            local affixes = ParseServerAffixes(affStr or "")
            local slot = tonumber(slotStr) or -1
            
            -- Store
            NolanUnidClient.State.inspectGrade = grade
            NolanUnidClient.State.inspectAffixes = affixes
            NolanUnidClient.State.inspectSlot = slot
            
            -- Directly append to tooltip if still showing for the correct slot
            if GameTooltip and GameTooltip:IsShown() and #affixes > 0 then
                local matchSlot = (NolanUnidClient.State.inspectWaitingSlot == slot)
                if matchSlot then
                    if grade > 0 then
                        local _, itemLink = GameTooltip:GetItem()
                        local color = GetDisplayColor(grade, itemLink)
                        local prefix = GRADE_PREFIXES[grade] or ""
                        for i = 1, GameTooltip:NumLines() do
                            local line = _G["GameTooltipTextLeft" .. i]
                            if line and line:GetText() and line:GetText() ~= "" then
                                local origName = line:GetText()
                                -- Don't re-prefix if already prefixed
                                if not string.find(origName, prefix) then
                                    line:SetText(color .. prefix .. origName .. "|r")
                                end
                                break
                            end
                        end
                    end
                    GameTooltip:AddLine(" ")
                    GameTooltip:AddLine("|cFFFFFF00诺兰祝福|r")
                    for _, a in ipairs(affixes) do
                        local displayName = string.gsub(a.affix_name, "%+%d+", "")
                        GameTooltip:AddDoubleLine("  " .. displayName .. " +" .. a.rolled_value, "", 0.2, 1.0, 0.2, 0, 0, 0)
                    end
                    GameTooltip:Show()
                end
            end

        elseif key == "EnchantStats" then
            local ilvlStr, statsStr = strsplit("|", data or "", 2)
            NolanUnidClient.EnchantStats.bonusIlvl = tonumber(ilvlStr) or 0
            NolanUnidClient.EnchantStats.stats = {}
            if statsStr and statsStr ~= "" then
                for pair in string.gmatch(statsStr, "[^,]+") do
                    local sid, val = string.match(pair, "(%d+):(%d+)")
                    if sid and val then
                        NolanUnidClient.EnchantStats.stats[tonumber(sid)] = tonumber(val)
                    end
                end
            end
            NolanUnidClient:UpdateStatPanel()
            AIO.Handle("NolanUnid", "QueryAllGS")

        elseif key == "AllGS" then
            local totalStr, partsStr = strsplit(":", data or "", 2)
            NolanUnidClient.State.totalGS = tonumber(totalStr) or 0
            NolanUnidClient:UpdateStatPanel()
            if PaperDollGS then PaperDollGS:SetText("GS: " .. NolanUnidClient.State.totalGS) end

        elseif key == "CacheSync" then
            -- Server sends cached enchant data on login
            -- Format: link1=grade1:affix1;link2=grade2:affix2;...
            -- where affix = name1:val1,name2:val2
            if not data or data == "" then return end
            for entry in string.gmatch(data, "[^;]+") do
                local link, rest = string.match(entry, "(|c%x+|Hitem:.-|h|r)=(.+)")
                if link and rest then
                    local gradeStr2, affStr2 = string.match(rest, "(%d+):(.*)")
                    if gradeStr2 and affStr2 then
                        local g = tonumber(gradeStr2) or 0
                        local affs = ParseServerAffixes(affStr2)
                        if #affs > 0 then
                            NolanUnidClient.ItemCache[link] = { grade = g, affixes = affs }
                        end
                    end
                end
            end

        elseif key == "InspectStats" then
            -- targetName|gs|statId:val,statId:val,...
            local targetName, rest = strsplit("|", data or "", 2)
            if not rest then return end
            local gsStr, statsStr = strsplit("|", rest, 2)
            local targetGS = tonumber(gsStr) or 0
            local targetStats = {}
            if statsStr and statsStr ~= "" then
                for pair in string.gmatch(statsStr, "[^,]+") do
                    local sid, val = string.match(pair, "(%d+):(%d+)")
                    if sid and val then targetStats[tonumber(sid)] = tonumber(val) end
                end
            end
            NolanUnidClient.State.inspectStatsData = targetStats
            NolanUnidClient.State.inspectGs = targetGS
            UpdatePanelValues(targetInspectPanel, targetGS, targetStats, false)

        elseif key == "SetAffix" then
            -- After identification: token:grade:entry:affixes
            -- Refresh cache
            if GameTooltip and GameTooltip:IsShown() then GameTooltip:Show() end
        end
    end)

    -- Initial requests
    AIO.Handle("NolanUnid", "QueryEnchantStats")
    AIO.Handle("NolanUnid", "QueryAllGS")
end)

-- ============ Bag Tooltip Hooks =====
-- Original ContainerFrame hook (for default bags)
hooksecurefunc("ContainerFrame_Update", function(frame)
    local name = frame:GetName()
    local bag = frame:GetID()
    local size = frame.size or 0
    for i = 1, size do
        local btn = _G[name .. "Item" .. i]
        if btn and not btn.__nu then
            btn:HookScript("OnEnter", function(self)
                local b = self:GetParent():GetID()
                local s = self:GetID()
                NolanUnidClient.State.lastTooltipAffixes = nil
                NolanUnidClient.State.lastGrade = 0
                AIO.Handle("NolanUnid", "QueryBagAffix", tostring(b), tostring(s))
            end)
            btn:HookScript("OnLeave", function()
                NolanUnidClient.State.lastTooltipAffixes = nil
                NolanUnidClient.State.lastGrade = 0
            end)
            btn.__nu = true
        end
    end
end)

-- Universal bag tooltip hook: works with ANY bag addon (Bagnon, ArkInventory, Combuctor, etc.)
hooksecurefunc(GameTooltip, "SetBagItem", function(tip, bag, slot)
    NolanUnidClient.State.lastTooltipAffixes = nil
    NolanUnidClient.State.lastGrade = 0
    AIO.Handle("NolanUnid", "QueryBagAffix", tostring(bag), tostring(slot))
end)

-- ============ Equip Slot Hooks =====
local EQUIP_SLOTS = {
    CharacterHeadSlot=0, CharacterNeckSlot=1, CharacterShoulderSlot=2,
    CharacterShirtSlot=3, CharacterChestSlot=4, CharacterWaistSlot=5,
    CharacterLegsSlot=6, CharacterFeetSlot=7, CharacterWristSlot=8,
    CharacterHandsSlot=9, CharacterFinger0Slot=10, CharacterFinger1Slot=11,
    CharacterTrinket0Slot=12, CharacterTrinket1Slot=13, CharacterBackSlot=14,
    CharacterMainHandSlot=15, CharacterSecondaryHandSlot=16,
    CharacterRangedSlot=17, CharacterTabardSlot=18,
}
for btnName, invSlot in pairs(EQUIP_SLOTS) do
    local btn = _G[btnName]
    if btn and not btn.__nu then
        btn:HookScript("OnEnter", function()
            NolanUnidClient.State.lastTooltipAffixes = nil
            NolanUnidClient.State.lastGrade = 0
            AIO.Handle("NolanUnid", "QueryEquipAffix", tostring(invSlot))
        end)
        btn:HookScript("OnLeave", function()
            NolanUnidClient.State.lastTooltipAffixes = nil
            NolanUnidClient.State.lastGrade = 0
        end)
        btn.__nu = true
    end
end

-- ============ Inspect Hooks =====
local INSPECT_SLOTS = {
    InspectHeadSlot=0, InspectNeckSlot=1, InspectShoulderSlot=2,
    InspectShirtSlot=3, InspectChestSlot=4, InspectWaistSlot=5,
    InspectLegsSlot=6, InspectFeetSlot=7, InspectWristSlot=8,
    InspectHandsSlot=9, InspectFinger0Slot=10, InspectFinger1Slot=11,
    InspectTrinket0Slot=12, InspectTrinket1Slot=13, InspectBackSlot=14,
    InspectMainHandSlot=15, InspectSecondaryHandSlot=16,
    InspectRangedSlot=17, InspectTabardSlot=18,
}

local currentInspectTarget = nil
local inspectHooked = false

local function HookInspectSlots()
    if inspectHooked then return end
    for btnName, invSlot in pairs(INSPECT_SLOTS) do
        local btn = _G[btnName]
        if btn and not btn.__nuInspect then
            btn:HookScript("OnEnter", function()
                currentInspectTarget = InspectFrame and InspectFrame.unit or "target"
                local name = UnitName(currentInspectTarget)
                if name then
                    NolanUnidClient.State.inspectAffixes = nil
                    NolanUnidClient.State.inspectGrade = 0
                    NolanUnidClient.State.inspectSlot = invSlot
                    NolanUnidClient.State.inspectWaitingSlot = invSlot
                    AIO.Handle("NolanUnid", "QueryInspectAffix", name, tostring(invSlot))
                end
            end)
            btn:HookScript("OnLeave", function()
                NolanUnidClient.State.inspectAffixes = nil
                NolanUnidClient.State.inspectGrade = 0
                currentInspectTarget = nil
            end)
            btn.__nuInspect = true
        end
    end
    inspectHooked = true
end

-- Hook when InspectFrame opens
if InspectFrame then
    InspectFrame:HookScript("OnShow", HookInspectSlots)
else
    -- InspectFrame not loaded yet, watch for INSPECT_TALENT_READY or ADDON_LOADED
    local inspectFrame = CreateFrame("Frame")
    inspectFrame:RegisterEvent("ADDON_LOADED")
    inspectFrame:SetScript("OnEvent", function(self, event, addon)
        if InspectFrame then
            InspectFrame:HookScript("OnShow", HookInspectSlots)
            self:UnregisterEvent("ADDON_LOADED")
        end
    end)
end

-- ============ Tooltip: Enchant display (own items) =====
GameTooltip:HookScript("OnTooltipSetItem", function(tip)
    -- Own items only (inspect handled by InspectAffix AIO callback)
    local _, itemLink = tip:GetItem()
    if itemLink then
        local gs = CalcClientGS(itemLink)
        if gs > 0 then
            tip:AddLine("GS: " .. gs, 0.2, 0.8, 1)
        end
    end

    local affixes = NolanUnidClient.State.lastTooltipAffixes
    if not affixes or #affixes == 0 then
        -- Try cache
        if itemLink then
            local cached = NolanUnidClient.ItemCache[itemLink]
            if cached and cached.affixes and #cached.affixes > 0 then
                affixes = cached.affixes
                NolanUnidClient.State.lastGrade = cached.grade
            end
        end
    end
    if not affixes or #affixes == 0 then return end
    local grade = NolanUnidClient.State.lastGrade or 0

    for i = 1, tip:NumLines() do
        local line = _G["GameTooltipTextLeft" .. i]
        if line and line:GetText() then
            local txt = line:GetText()
            if string.find(txt, "需要鉴定") or string.find(txt, "显露真正力量") then line:SetText("") end
        end
    end

    if grade > 0 then
        local color = GetDisplayColor(grade, itemLink)
        local prefix = GRADE_PREFIXES[grade] or ""
        for i = 1, tip:NumLines() do
            local line = _G["GameTooltipTextLeft" .. i]
            if line and line:GetText() and line:GetText() ~= "" then
                local origName = line:GetText()
                line:SetText(color .. prefix .. string.gsub(origName, "未鉴定的", "") .. "|r")
                break
            end
        end
    end

    tip:AddLine(" ")
    tip:AddLine("|cFFFFFF00诺兰祝福|r")
    for _, a in ipairs(affixes) do
        local displayName = string.gsub(a.affix_name, "%+%d+", "")
        tip:AddDoubleLine("  " .. displayName .. " +" .. a.rolled_value, "", 0.2, 1.0, 0.2, 0, 0, 0)
    end
    tip:Show()
end)

-- ============ PaperDoll GS =====
PaperDollGS = PaperDollFrame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
PaperDollGS:SetPoint("TOPRIGHT", PaperDollFrame, "TOPRIGHT", -40, -35)
PaperDollGS:SetJustifyH("RIGHT")
PaperDollGS:SetText("GS: 0")

-- ============ Stat Panel =====
local statPanel = nil

local STAT_LAYOUT = {
    {cat="基础属性"},
    {stat=1, name="力量",   enchantId=4},
    {stat=2, name="敏捷",   enchantId=3},
    {stat=3, name="耐力",   enchantId=7},
    {stat=4, name="智力",   enchantId=5},
    {stat=5, name="精神",   enchantId=6},
    {armor=true, name="护甲"},
    {cat="近战"},
    {name="攻击强度", apType="melee"},
    {stat=32, name="命中等级"},
    {stat=33, name="暴击等级"},
    {stat=31, name="急速等级"},
    {stat=44, name="护甲穿透"},
    {stat=37, name="精准等级"},
    {cat="远程"},
    {name="攻击强度", apType="ranged"},
    {cat="法术"},
    {stat=45, name="法术强度"},
    {stat=127, name="法术强度"},
    {cat="防御"},
    {stat=12, name="防御等级"},
    {dodge=true, name="躲闪"},
    {parry=true, name="招架"},
    {block=true, name="格挡"},
    {resilience=true, name="韧性"},
    {cat=""},
    {gs=true},
}

function NolanUnidClient:CreateStatPanel()
    if statPanel then return end
    statPanel = CreateFrame("Frame", "NolanStatPanel", UIParent)
    statPanel:SetWidth(190)
    statPanel:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        tile = true, tileSize = 16, edgeSize = 12,
        insets = { left = 4, right = 4, top = 4, bottom = 4 }
    })
    statPanel:SetBackdropColor(0, 0, 0, 0.95)
    statPanel:SetPoint("TOPLEFT", CharacterFrame, "TOPRIGHT", 2, 0)
    statPanel:SetHeight(600)
    statPanel:SetClampedToScreen(true)
    statPanel:SetMovable(true)
    statPanel:EnableMouse(true)
    statPanel:RegisterForDrag("LeftButton")
    statPanel:SetScript("OnDragStart", function(self) self:StartMoving() end)
    statPanel:SetScript("OnDragStop", function(self) self:StopMovingOrSizing() end)
    statPanel:Hide()

    local title = statPanel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", statPanel, "TOPLEFT", 12, -12)
    title:SetText("诺兰属性面板")

    local lastAnchor = title
    statPanel.lines = {}

    for i, entry in ipairs(STAT_LAYOUT) do
        if entry.cat then
            local line = statPanel:CreateFontString(nil, "OVERLAY", "GameFontDisable")
            line:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 0, entry.cat == "" and -8 or -6)
            line:SetText(entry.cat ~= "" and ("|cFFD3A14A" .. entry.cat .. "|r") or "")
            lastAnchor = line
        elseif entry.gs then
            local gsLine = statPanel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
            gsLine:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 12, -6)
            gsLine:SetText("GS: 0")
            statPanel.gsLine = gsLine
            lastAnchor = gsLine
        else
            local lineFrame = CreateFrame("Frame", nil, statPanel)
            lineFrame:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 0, -2)
            lineFrame:SetHeight(18)
            lineFrame:SetWidth(170)
            local label = lineFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
            label:SetPoint("LEFT", lineFrame, "LEFT", 4, 0)
            label:SetJustifyH("LEFT")
            local value = lineFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
            value:SetPoint("RIGHT", lineFrame, "RIGHT", -4, 0)
            value:SetJustifyH("RIGHT")
            lineFrame.label = label
            lineFrame.value = value
            lineFrame.entry = entry
            lineFrame:SetScript("OnEnter", function(self)
                if self.tipLines then
                    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
                    GameTooltip:ClearLines()
                    for _, tl in ipairs(self.tipLines) do GameTooltip:AddLine(tl, 1, 1, 1, true) end
                    GameTooltip:Show()
                end
            end)
            lineFrame:SetScript("OnLeave", function() GameTooltip:Hide() end)
            table.insert(statPanel.lines, lineFrame)
            lastAnchor = lineFrame
        end
    end

    CharacterFrame:HookScript("OnShow", function()
        statPanel:Show()
        if AIO and AIO.Handle then
            AIO.Handle("NolanUnid", "QueryEnchantStats")
            AIO.Handle("NolanUnid", "QueryAllGS")
        end
    end)
    CharacterFrame:HookScript("OnHide", function() statPanel:Hide() end)
end

local function GetStatValue(statIdx)
    local effective, base = UnitStat("player", statIdx)
    return math.floor((effective or 0) + 0.5), math.floor((base or 0) + 0.5)
end

function NolanUnidClient:UpdateStatPanel()
    if not statPanel then self:CreateStatPanel() end
    if not statPanel then return end
    local enchantStats = self.EnchantStats.stats

    for _, lineFrame in ipairs(statPanel.lines) do
        local entry = lineFrame.entry
        local val = ""
        local tipLines = nil

        if entry.stat and entry.stat <= 5 then
            local total, base = GetStatValue(entry.stat)
            local bonus = total - base
            local enchantBonus = enchantStats[entry.enchantId] or 0
            val = "|cff00ff00" .. total .. "|r"
            if bonus > 0 or enchantBonus > 0 then
                tipLines = { entry.name .. ": " .. total, "  基础: " .. base, "  装备加成: +" .. (bonus - enchantBonus) }
                if enchantBonus > 0 then table.insert(tipLines, "  |cff00ff00诺兰祝福: +" .. enchantBonus .. "|r") end
            end
        elseif entry.armor then
            local base, effective = UnitArmor("player")
            val = "|cffffffff" .. (effective or base or 0) .. "|r"
        elseif entry.dodge then
            val = string.format("|cffffffff%.2f%%|r", GetDodgeChance() or 0)
        elseif entry.parry then
            val = string.format("|cffffffff%.2f%%|r", GetParryChance() or 0)
        elseif entry.block then
            val = string.format("|cffffffff%.2f%%|r", GetBlockChance() or 0)
        elseif entry.resilience then
            val = string.format("|cffffffff%.2f%%|r", GetCombatRatingBonus(15) or 0)
        elseif entry.apType then
            local baseAP, posAP, negAP
            if entry.apType == "melee" then baseAP, posAP, negAP = UnitAttackPower("player")
            else baseAP, posAP, negAP = UnitRangedAttackPower("player") end
            local total = (baseAP or 0) + (posAP or 0) + (negAP or 0)
            local enchantAP = (enchantStats[0] or 0) + (enchantStats[38] or 0)
            val = "|cffffffff" .. total .. "|r"
            if enchantAP > 0 then tipLines = { entry.name .. ": " .. total, "  基础: " .. (total - enchantAP), "  |cff00ff00诺兰祝福: +" .. enchantAP .. "|r" } end
        elseif entry.stat then
            local enchantVal = enchantStats[entry.stat] or 0
            if enchantVal > 0 then val = "|cff00ff00+" .. enchantVal .. "|r"; tipLines = { entry.name .. ": +" .. enchantVal, "  |cff00ff00诺兰祝福|r" }
            else val = "|cff8888880|r" end
        end

        lineFrame.label:SetText(entry.name or "")
        lineFrame.value:SetText(val)
        lineFrame.tipLines = tipLines
    end

    if statPanel.gsLine then
        statPanel.gsLine:SetText("GS: " .. (self.State.totalGS or 0))
    end
end

local refreshFrame = CreateFrame("Frame")
refreshFrame:RegisterEvent("UNIT_STATS")
refreshFrame:SetScript("OnEvent", function(self, event, unit)
    if unit == "player" and statPanel and statPanel:IsVisible() then
        NolanUnidClient:UpdateStatPanel()
    end
end)

SLASH_NOLANUNID1 = "/nu"
SlashCmdList["NOLANUNID"] = function()
    (message or print)("[NU v"..VERSION.."] GS="..tostring(NolanUnidClient.State.totalGS or 0).." Cached="..tostring(NolanUnidClient.ItemCache and next(NolanUnidClient.ItemCache) and "yes" or "no"))
end

-- ============ Inspect Stat Panel ============
-- inspectPanel removed (using myInspectPanel/targetInspectPanel)

local INSPECT_STAT_LAYOUT = {
    {cat="诺兰祝福属性"},
    {stat=4, name="力量"},
    {stat=3, name="敏捷"},
    {stat=7, name="耐力"},
    {stat=5, name="智力"},
    {stat=6, name="精神"},
    {stat=32, name="命中等级"},
    {stat=33, name="暴击等级"},
    {stat=36, name="急速等级"},
    {stat=44, name="护甲穿透"},
    {stat=38, name="攻击强度"},
    {stat=45, name="法术强度"},
    {cat=""},
    {gs=true},
}

local STAT_NAMES = {
    [4]="力量", [3]="敏捷", [7]="耐力", [5]="智力", [6]="精神",
    [32]="命中等级", [33]="暴击等级", [36]="急速等级", [44]="护甲穿透",
    [38]="攻击强度", [45]="法术强度",
}


-- ============ Inspect Stat Panels ============
local STAT_NAMES = {
    [4]="力量", [3]="敏捷", [7]="耐力", [5]="智力", [6]="精神",
    [32]="命中等级", [33]="暴击等级", [36]="急速等级", [44]="护甲穿透",
    [38]="攻击强度", [45]="法术强度",
}

local function BuildPanelLayout(panel, titleText)
    local title = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", panel, "TOPLEFT", 12, -12)
    title:SetText(titleText)

    local lastAnchor = title
    panel.lines = {}

    for i, entry in ipairs(STAT_LAYOUT) do
        if entry.cat then
            local line = panel:CreateFontString(nil, "OVERLAY", "GameFontDisable")
            line:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 0, entry.cat == "" and -8 or -6)
            line:SetText(entry.cat ~= "" and ("|cFFD3A14A" .. entry.cat .. "|r") or "")
            lastAnchor = line
        elseif entry.gs then
            local gsLine = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
            gsLine:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 12, -6)
            gsLine:SetText("GS: 0")
            panel.gsLine = gsLine
            lastAnchor = gsLine
        else
            local lineFrame = CreateFrame("Frame", nil, panel)
            lineFrame:SetPoint("TOPLEFT", lastAnchor, "BOTTOMLEFT", 0, -2)
            lineFrame:SetHeight(18)
            lineFrame:SetWidth(170)
            local label = lineFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
            label:SetPoint("LEFT", lineFrame, "LEFT", 4, 0)
            label:SetJustifyH("LEFT")
            local value = lineFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
            value:SetPoint("RIGHT", lineFrame, "RIGHT", -4, 0)
            value:SetJustifyH("RIGHT")
            lineFrame.label = label
            lineFrame.value = value
            lineFrame.entry = entry
            table.insert(panel.lines, lineFrame)
            lastAnchor = lineFrame
        end
    end
end

local function UpdatePanelValues(panel, gs, enchantStats, isSelf)
    if not panel then return end
    if panel.gsLine then panel.gsLine:SetText("GS: " .. gs) end
    for _, lineFrame in ipairs(panel.lines) do
        local entry = lineFrame.entry
        local val = ""
        if entry.stat and entry.stat <= 5 then
            if isSelf then
                local total, base = GetStatValue(entry.stat)
                val = "|cff00ff00" .. total .. "|r"
            else
                local eb = enchantStats[entry.enchantId] or 0
                if eb > 0 then val = "|cff00ff00+" .. eb .. "|r" else val = "|cff888888--|r" end
            end
        elseif entry.armor then
            if isSelf then
                local b2, e2 = UnitArmor("player")
                val = "|cffffffff" .. (e2 or b2 or 0) .. "|r"
            else val = "|cff888888--|r" end
        elseif entry.dodge then
            if isSelf then val = string.format("|cffffffff%.2f%%|r", GetDodgeChance() or 0)
            else val = "|cff888888--|r" end
        elseif entry.parry then
            if isSelf then val = string.format("|cffffffff%.2f%%|r", GetParryChance() or 0)
            else val = "|cff888888--|r" end
        elseif entry.block then
            if isSelf then val = string.format("|cffffffff%.2f%%|r", GetBlockChance() or 0)
            else val = "|cff888888--|r" end
        elseif entry.resilience then
            if isSelf then val = string.format("|cffffffff%.2f%%|r", GetCombatRatingBonus(15) or 0)
            else val = "|cff888888--|r" end
        elseif entry.apType then
            if isSelf then
                local bAP, pAP, nAP
                if entry.apType == "melee" then bAP, pAP, nAP = UnitAttackPower("player")
                else bAP, pAP, nAP = UnitRangedAttackPower("player") end
                val = "|cffffffff" .. ((bAP or 0)+(pAP or 0)+(nAP or 0)) .. "|r"
            else
                local eAP = (enchantStats[0] or 0) + (enchantStats[38] or 0)
                if eAP > 0 then val = "|cff00ff00+" .. eAP .. "|r" else val = "|cff888888--|r" end
            end
        elseif entry.stat then
            local ev = enchantStats[entry.stat] or 0
            if ev > 0 then val = "|cff00ff00+" .. ev .. "|r" else val = "|cff888888--|r" end
        end
        lineFrame.label:SetText(entry.name or "")
        lineFrame.value:SetText(val)
    end
end

local myInspectPanel = nil
local targetInspectPanel = nil

local function CreateInspectPanels()
    if myInspectPanel or not InspectFrame then return end

    -- Target panel: right of InspectFrame
    targetInspectPanel = CreateFrame("Frame", "NolanTargetPanel", UIParent)
    targetInspectPanel:SetWidth(190)
    targetInspectPanel:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        tile = true, tileSize = 16, edgeSize = 12,
        insets = { left = 4, right = 4, top = 4, bottom = 4 }
    })
    targetInspectPanel:SetBackdropColor(0, 0, 0, 0.95)
    targetInspectPanel:SetPoint("TOPLEFT", InspectFrame, "TOPRIGHT", 2, 0)
    targetInspectPanel:SetHeight(600)
    targetInspectPanel:SetClampedToScreen(true)
    targetInspectPanel:SetMovable(true)
    targetInspectPanel:EnableMouse(true)
    targetInspectPanel:RegisterForDrag("LeftButton")
    targetInspectPanel:SetScript("OnDragStart", function(self) self:StartMoving() end)
    targetInspectPanel:SetScript("OnDragStop", function(self) self:StopMovingOrSizing() end)
    targetInspectPanel:Hide()
    BuildPanelLayout(targetInspectPanel, "|cFFD3A14A对方属性|r")

    -- My panel: left of InspectFrame
    myInspectPanel = CreateFrame("Frame", "NolanMyInspectPanel", UIParent)
    myInspectPanel:SetWidth(190)
    myInspectPanel:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        tile = true, tileSize = 16, edgeSize = 12,
        insets = { left = 4, right = 4, top = 4, bottom = 4 }
    })
    myInspectPanel:SetBackdropColor(0, 0, 0, 0.95)
    myInspectPanel:SetPoint("TOPRIGHT", InspectFrame, "TOPLEFT", -2, 0)
    myInspectPanel:SetHeight(600)
    myInspectPanel:SetClampedToScreen(true)
    myInspectPanel:SetMovable(true)
    myInspectPanel:EnableMouse(true)
    myInspectPanel:RegisterForDrag("LeftButton")
    myInspectPanel:SetScript("OnDragStart", function(self) self:StartMoving() end)
    myInspectPanel:SetScript("OnDragStop", function(self) self:StopMovingOrSizing() end)
    myInspectPanel:Hide()
    BuildPanelLayout(myInspectPanel, "|cFFD3A14A我的属性|r")

    InspectFrame:HookScript("OnShow", function()
        UpdatePanelValues(myInspectPanel, NolanUnidClient.State.totalGS or 0, NolanUnidClient.EnchantStats.stats or {}, true)
        myInspectPanel:Show()
        UpdatePanelValues(targetInspectPanel, 0, {}, false)
        targetInspectPanel:Show()
        local targetName = UnitName(InspectFrame.unit or "target")
        if targetName and AIO and AIO.Handle then
            AIO.Handle("NolanUnid", "QueryInspectStats", targetName)
        end
    end)
    InspectFrame:HookScript("OnHide", function()
        myInspectPanel:Hide()
        targetInspectPanel:Hide()
    end)
end

-- Create panels when InspectFrame is available
local inspectSetupFrame = CreateFrame("Frame")
inspectSetupFrame:SetScript("OnUpdate", function(self, elapsed)
    self.elapsed = (self.elapsed or 0) + elapsed
    if self.elapsed < 1 then return end
    self.elapsed = 0
    if InspectFrame then
        CreateInspectPanels()
        self:Hide()
    end
end)
