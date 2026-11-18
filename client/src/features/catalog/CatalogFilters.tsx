import { Checkbox, FormControl, FormControlLabel, FormGroup, FormLabel, Stack } from '@mui/material';
import { categoryDisplay } from './categoryLabels';

interface Props {
  brands: string[];
  types: string[];
  selectedBrands: string[];
  selectedTypes: string[];
  onBrandsChange: (brands: string[]) => void;
  onTypesChange: (types: string[]) => void;
}

function FilterGroup({ label, options, selected, optionLabel, onChange }: {
  label: string;
  options: string[];
  selected: string[];
  optionLabel: (option: string) => string;
  onChange: (values: string[]) => void;
}) {
  if (!options.length) return null;
  return (
    <FormControl component="fieldset" sx={{ minWidth: 0 }}>
      <FormLabel component="legend" sx={{ fontWeight: 700 }}>{label}</FormLabel>
      <FormGroup>
        {options.map(option => (
          <FormControlLabel key={option} label={<span dir="auto">{optionLabel(option)}</span>}
            sx={{ m: 0, overflowWrap: 'anywhere', '& .MuiCheckbox-root': { p: 0.75, minHeight: 42, minWidth: 42 } }}
            control={<Checkbox checked={selected.includes(option)} onChange={(_, checked) =>
              onChange(checked ? [...selected, option] : selected.filter(value => value !== option))} />} />
        ))}
      </FormGroup>
    </FormControl>
  );
}

export default function CatalogFilters(props: Props) {
  return (
    <Stack spacing={2}>
      <FilterGroup label="دسته‌بندی" options={props.types} selected={props.selectedTypes} optionLabel={type => categoryDisplay(type).label} onChange={props.onTypesChange} />
      <FilterGroup label="برند" options={props.brands} selected={props.selectedBrands} optionLabel={brand => brand} onChange={props.onBrandsChange} />
    </Stack>
  );
}
