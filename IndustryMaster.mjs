/*!
 * Cities: Skylines II UI Module
 *
 * Id: IndustryMaster
 * Author: Local build
 * Version: 1.0
 * Dependencies:
 */

const sectionsPath = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
const companyType = "Game.UI.InGame.CompanySection";
const rowPath = "game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.tsx";
const localizationPath = "game-ui/common/localization/loc.generated.ts";
const numberPath = "game-ui/common/localization/localized-number.tsx";
const unitPath = "game-ui/common/localization/unit.ts";

const incomeLayout = {
    display: "flex",
    flexWrap: "wrap",
    justifyContent: "flex-end",
    alignItems: "baseline",
    minWidth: 0,
};

export default function register(mod) {
    const { registry } = mod;
    const React = window.React;
    const incomeTitle = registry.get(localizationPath)?.Loc?.SelectedInfoPanel?.COMPANY_INCOME;
    const LocalizedNumber = registry.get(numberPath)?.LocalizedNumber;
    const money = registry.get(unitPath)?.Unit?.Money;
    const sections = registry.get(sectionsPath)?.selectedInfoSectionComponents;
    if (!React || !incomeTitle || !LocalizedNumber || !money || !sections?.[companyType]
        || !registry.get(rowPath)?.InfoRow) {
        console.warn("IndustryMaster: company income display is unavailable for this UI build.");
        return;
    }

    const { createContext, createElement, useContext } = React;
    const Receipt = createContext(null);

    mod.extend(sectionsPath, "selectedInfoSectionComponents", components => {
        const NativeCompany = components[companyType];
        function CompanyWithReceipt(props) {
            const amount = props.industryMasterVirtualIncome;
            const receipt = Number.isSafeInteger(amount) && amount > 0
                ? { amount, label: props.industryMasterVirtualIncomeLabel }
                : null;
            return createElement(Receipt.Provider, { value: receipt }, createElement(NativeCompany, props));
        }
        return { ...components, [companyType]: CompanyWithReceipt };
    });

    mod.extend(rowPath, "InfoRow", NativeRow => function IncomeWithReceipt(props) {
        const receipt = useContext(Receipt);
        if (!receipt || props.left?.type !== incomeTitle || !props.right)
            return createElement(NativeRow, props);

        const supplement = createElement("span", null, "+（",
            createElement(LocalizedNumber, { value: receipt.amount, unit: money }),
            " ", receipt.label, "）");
        const right = createElement("span", { style: incomeLayout }, props.right, supplement);
        return createElement(NativeRow, { ...props, right });
    });
}
