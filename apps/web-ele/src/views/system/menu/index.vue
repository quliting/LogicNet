<script lang="ts" setup>
import type { FormInstance, FormRules, TableInstance } from 'element-plus';

import type {
  BackendMenuItem,
  MenuTypeName,
  SaveMenuParams,
} from '#/api/core/menu';

import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue';

import {
  ElButton,
  ElCard,
  ElCol,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElRow,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTreeSelect,
} from 'element-plus';

import {
  createMenuApi,
  deleteMenuApi,
  getMenuDetailApi,
  getMenuTreeApi,
  updateMenuApi,
} from '#/api/core/menu';

import RoleMenuDrawer from './components/RoleMenuDrawer.vue';

defineOptions({ name: 'SystemMenu' });

const ROOT_PARENT_ID = '0';

const typeOptions: Array<{ label: string; value: MenuTypeName }> = [
  { label: 'Directory（目录）', value: 'Directory' },
  { label: 'Menu（菜单）', value: 'Menu' },
  { label: 'Button（按钮）', value: 'Button' },
];

const typeTagType: Record<MenuTypeName, 'info' | 'success' | 'warning'> = {
  Directory: 'info',
  Menu: 'success',
  Button: 'warning',
};

interface FilterState {
  isEnabled: '' | 'false' | 'true';
  name: string;
  type: '' | MenuTypeName;
}

interface MenuFormState {
  Component: string;
  Icon: string;
  IsEnabled: boolean;
  IsVisible: boolean;
  Name: string;
  ParentId: string;
  Path: string;
  PermissionCode: string;
  Sort: number;
  Type: MenuTypeName;
}

const treeData = ref<BackendMenuItem[]>([]);
const loading = ref(false);
const submitting = ref(false);
const dialogVisible = ref(false);
const roleMenuVisible = ref(false);
const editingId = ref('');
const parentName = ref('');
const allExpanded = ref(true);
const formRef = ref<FormInstance>();
const tableRef = ref<TableInstance>();

const filterModel = reactive<FilterState>({
  isEnabled: '',
  name: '',
  type: '',
});
const appliedFilter = reactive<FilterState>({
  isEnabled: '',
  name: '',
  type: '',
});

function createDefaultForm(): MenuFormState {
  return {
    ParentId: ROOT_PARENT_ID,
    Name: '',
    Path: '',
    Component: '',
    Icon: '',
    Sort: 0,
    Type: 'Menu',
    PermissionCode: '',
    IsEnabled: true,
    IsVisible: true,
  };
}

const form = reactive<MenuFormState>(createDefaultForm());

const rules: FormRules<MenuFormState> = {
  Name: [{ required: true, message: '请输入菜单名称', trigger: 'blur' }],
  Path: [
    {
      validator: (_rule, value, callback) => {
        const path = (value ?? '').trim();
        // 按钮不要求路由路径；其他类型必填且需以 / 开头
        if (form.Type !== 'Button' && !path) {
          callback(new Error('请输入路由路径'));
          return;
        }
        if (path && !path.startsWith('/')) {
          callback(new Error('路由路径需以 / 开头'));
          return;
        }
        callback();
      },
      trigger: 'blur',
    },
  ],
  Type: [{ required: true, message: '请选择菜单类型', trigger: 'change' }],
  PermissionCode: [
    {
      validator: (_rule, value, callback) => {
        if (form.Type === 'Button' && !(value ?? '').trim()) {
          callback(new Error('按钮类型必须填写权限码'));
          return;
        }
        callback();
      },
      trigger: 'blur',
    },
  ],
};

watch(
  () => form.Type,
  () => {
    void nextTick(() => {
      formRef.value?.clearValidate(['Path', 'Component', 'PermissionCode']);
    });
  },
);

const dialogTitle = computed(() => {
  if (editingId.value) return '编辑菜单';
  return parentName.value ? `在『${parentName.value}』下新增` : '新增菜单';
});

/**
 * 树选择数据：顶级使用伪节点 ParentId="0"；
 * 编辑时排除自身，避免把上级指向自己或自己的后代。
 */
function buildParentTree(
  nodes: BackendMenuItem[],
  excludeId: string,
): BackendMenuItem[] {
  return nodes
    .filter((node) => node.Id !== excludeId)
    .map((node) => ({
      ...node,
      Children: node.Children
        ? buildParentTree(node.Children, excludeId)
        : [],
    }));
}

const parentTree = computed(() => [
  {
    Id: ROOT_PARENT_ID,
    Name: '顶级菜单',
    Children: buildParentTree(treeData.value, editingId.value),
  },
]);

function getTypeTagType(type: MenuTypeName) {
  return typeTagType[type] ?? 'info';
}

function matchFilter(node: BackendMenuItem) {
  const keyword = appliedFilter.name.trim();
  const nameMatched = !keyword || node.Name.includes(keyword);
  const typeMatched = !appliedFilter.type || node.Type === appliedFilter.type;
  const enabledMatched =
    appliedFilter.isEnabled === '' ||
    node.IsEnabled === (appliedFilter.isEnabled === 'true');
  return nameMatched && typeMatched && enabledMatched;
}

/** 客户端过滤：命中节点保留；未命中但有命中子节点时保留父级作为层级上下文 */
function filterTree(nodes: BackendMenuItem[]): BackendMenuItem[] {
  const result: BackendMenuItem[] = [];
  for (const node of nodes) {
    const children = filterTree(node.Children ?? []);
    if (matchFilter(node) || children.length > 0) {
      result.push({
        ...node,
        Children: children.length > 0 ? children : undefined,
      });
    }
  }
  return result;
}

const tableData = computed(() => filterTree(treeData.value));

/** 查找节点到根的路径（含节点自身），用于展开祖先层级 */
function findNodePath(
  nodes: BackendMenuItem[],
  id: string,
  trail: BackendMenuItem[] = [],
): BackendMenuItem[] | null {
  for (const node of nodes) {
    const next = [...trail, node];
    if (node.Id === id) return next;
    if (node.Children?.length) {
      const found = findNodePath(node.Children, id, next);
      if (found) return found;
    }
  }
  return null;
}

/** 展开指定节点的所有祖先，确保新增行可见 */
async function expandPathTo(id: string) {
  await nextTick();
  const path = findNodePath(tableData.value, id);
  path?.forEach((node) => {
    tableRef.value?.toggleRowExpansion(node, true);
  });
}

/** 一键展开/折叠全部 */
function toggleExpandAll() {
  const target = !allExpanded.value;
  allExpanded.value = target;
  const walk = (nodes: BackendMenuItem[]) => {
    for (const node of nodes) {
      tableRef.value?.toggleRowExpansion(node, target);
      if (node.Children?.length) walk(node.Children);
    }
  };
  walk(tableData.value);
}

async function loadTree() {
  loading.value = true;
  try {
    treeData.value = (await getMenuTreeApi()) ?? [];
  } catch {
    // 业务失败文案由请求拦截器统一提示
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  Object.assign(appliedFilter, { ...filterModel });
  void loadTree();
}

function handleReset() {
  filterModel.name = '';
  filterModel.type = '';
  filterModel.isEnabled = '';
  Object.assign(appliedFilter, { ...filterModel });
  void loadTree();
}

async function openCreate() {
  editingId.value = '';
  parentName.value = '';
  Object.assign(form, createDefaultForm());
  dialogVisible.value = true;
  await nextTick();
  formRef.value?.clearValidate();
}

/** 行内添加子级：预填 ParentId，Type 默认 Menu */
async function openCreateChild(row: BackendMenuItem) {
  editingId.value = '';
  parentName.value = row.Name;
  Object.assign(form, createDefaultForm(), {
    ParentId: row.Id,
    Type: 'Menu',
  });
  dialogVisible.value = true;
  await nextTick();
  formRef.value?.clearValidate();
}

async function openEdit(row: BackendMenuItem) {
  try {
    const detail = await getMenuDetailApi(row.Id);
    editingId.value = row.Id;
    parentName.value = '';
    Object.assign(form, {
      ParentId: detail.ParentId,
      Name: detail.Name,
      Path: detail.Path,
      Component: detail.Component ?? '',
      Icon: detail.Icon ?? '',
      Sort: detail.Sort,
      Type: detail.Type,
      PermissionCode: detail.PermissionCode ?? '',
      IsEnabled: detail.IsEnabled,
      IsVisible: detail.IsVisible,
    });
    dialogVisible.value = true;
    await nextTick();
    formRef.value?.clearValidate();
  } catch {
    // 详情加载失败由请求拦截器提示，不打开弹窗
  }
}

async function submitForm() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;

  submitting.value = true;
  const parentForExpand = form.ParentId;
  try {
    const payload: SaveMenuParams = {
      ParentId: form.ParentId || ROOT_PARENT_ID,
      Name: form.Name.trim(),
      Path: form.Path.trim(),
      Component: form.Component.trim() || null,
      Icon: form.Icon.trim() || null,
      Sort: form.Sort,
      Type: form.Type,
      PermissionCode: form.PermissionCode.trim() || null,
      IsEnabled: form.IsEnabled,
      IsVisible: form.IsVisible,
    };

    if (editingId.value) {
      await updateMenuApi({ ...payload, Id: editingId.value });
      ElMessage.success('菜单已更新');
    } else {
      await createMenuApi(payload);
      ElMessage.success('菜单已创建');
    }

    dialogVisible.value = false;
    await loadTree();
    // 保存后展开父级链路，确保新增/更新行在树表中可见
    if (parentForExpand && parentForExpand !== ROOT_PARENT_ID) {
      await expandPathTo(parentForExpand);
    }
  } catch {
    // 业务失败（HTTP 200 + Code 400）由请求拦截器读取 Errors 并提示，不提示成功
  } finally {
    submitting.value = false;
  }
}

async function handleDelete(row: BackendMenuItem) {
  try {
    await ElMessageBox.confirm(
      `确定删除菜单「${row.Name}」吗？删除后不可恢复。`,
      '删除确认',
      {
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        type: 'warning',
      },
    );
  } catch {
    return;
  }

  try {
    await deleteMenuApi(row.Id);
    ElMessage.success('菜单已删除');
    await loadTree();
  } catch {
    // 存在子节点等业务失败由请求拦截器提示 Errors 文案，不能静默成功
  }
}

onMounted(() => {
  void loadTree();
});
</script>

<template>
  <div class="p-5">
    <ElCard shadow="never">
      <template #header>
        <div class="flex items-center justify-between">
          <span>菜单管理</span>
          <div class="flex items-center gap-2">
            <ElButton @click="toggleExpandAll">
              {{ allExpanded ? '全部折叠' : '全部展开' }}
            </ElButton>
            <ElButton @click="roleMenuVisible = true">角色菜单授权</ElButton>
            <ElButton type="primary" @click="openCreate">新增菜单</ElButton>
          </div>
        </div>
      </template>

      <ElForm :inline="true" :model="filterModel" class="mb-4">
        <ElFormItem label="名称">
          <ElInput
            v-model="filterModel.name"
            clearable
            placeholder="支持模糊匹配"
            @keyup.enter="handleSearch"
          />
        </ElFormItem>
        <ElFormItem label="Type">
          <ElSelect
            v-model="filterModel.type"
            clearable
            placeholder="全部类型"
            style="width: 180px"
          >
            <ElOption
              v-for="item in typeOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="是否启用">
          <ElSelect
            v-model="filterModel.isEnabled"
            clearable
            placeholder="全部状态"
            style="width: 140px"
          >
            <ElOption label="启用" value="true" />
            <ElOption label="停用" value="false" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" @click="handleSearch">查询</ElButton>
          <ElButton @click="handleReset">重置</ElButton>
        </ElFormItem>
      </ElForm>

      <ElTable
        ref="tableRef"
        v-loading="loading"
        :data="tableData"
        row-key="Id"
        :tree-props="{ children: 'Children' }"
        default-expand-all
        border
      >
        <ElTableColumn label="名称" prop="Name" min-width="200">
          <template #default="{ row }">
            <span>{{ row.Name }}</span>
            <span v-if="row.Icon" class="ml-1 text-xs text-gray-400">
              {{ row.Icon }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="Path" prop="Path" min-width="180" />
        <ElTableColumn label="Component" min-width="190">
          <template #default="{ row }">{{ row.Component || '-' }}</template>
        </ElTableColumn>
        <ElTableColumn label="Type" width="110">
          <template #default="{ row }">
            <ElTag
              :type="getTypeTagType(row.Type)"
              :effect="row.Type === 'Button' ? 'dark' : 'light'"
              size="small"
            >
              {{ row.Type }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="权限码" min-width="150">
          <template #default="{ row }">
            {{ row.PermissionCode || '-' }}
          </template>
        </ElTableColumn>
        <ElTableColumn label="排序" prop="Sort" width="70" />
        <ElTableColumn label="启用/可见" width="150">
          <template #default="{ row }">
            <ElTag :type="row.IsEnabled ? 'success' : 'danger'" size="small">
              {{ row.IsEnabled ? '启用' : '停用' }}
            </ElTag>
            <ElTag :type="row.IsVisible ? 'primary' : 'info'" size="small">
              {{ row.IsVisible ? '可见' : '隐藏' }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <ElButton
              link
              type="primary"
              @click="openCreateChild(row as BackendMenuItem)"
            >
              添加子级
            </ElButton>
            <ElButton link type="primary" @click="openEdit(row as BackendMenuItem)">
              编辑
            </ElButton>
            <ElButton
              link
              type="danger"
              @click="handleDelete(row as BackendMenuItem)"
            >
              删除
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="640px"
      append-to-body
      :close-on-click-modal="false"
      destroy-on-close
    >
      <ElForm
        ref="formRef"
        :model="form"
        :rules="rules"
        label-width="100px"
      >
        <ElFormItem label="上级菜单" prop="ParentId">
          <ElTreeSelect
            v-model="form.ParentId"
            :data="parentTree"
            :props="{ label: 'Name', children: 'Children' }"
            node-key="Id"
            check-strictly
            default-expand-all
            filterable
            placeholder="请选择上级菜单"
            style="width: 100%"
          />
        </ElFormItem>

        <ElRow :gutter="16">
          <ElCol :span="14">
            <ElFormItem label="名称" prop="Name">
              <ElInput
                v-model="form.Name"
                maxlength="50"
                placeholder="请输入菜单名称"
              />
            </ElFormItem>
          </ElCol>
          <ElCol :span="10">
            <ElFormItem label="排序" prop="Sort">
              <ElInputNumber
                v-model="form.Sort"
                :min="0"
                :max="9999"
                controls-position="right"
                class="!w-full"
              />
            </ElFormItem>
          </ElCol>
        </ElRow>

        <ElFormItem label="Path" prop="Path">
          <ElInput
            v-model="form.Path"
            placeholder="路由路径，如 /system/menu（按钮类型可留空）"
          />
        </ElFormItem>

        <ElFormItem label="Component" prop="Component">
          <ElInput
            v-model="form.Component"
            placeholder="前端组件标识，目录可为空，如 system/menu/index"
          />
        </ElFormItem>

        <ElRow :gutter="16">
          <ElCol :span="12">
            <ElFormItem label="Icon" prop="Icon">
              <ElInput v-model="form.Icon" placeholder="如 carbon:settings" />
            </ElFormItem>
          </ElCol>
          <ElCol :span="12">
            <ElFormItem label="Type" prop="Type">
              <ElSelect v-model="form.Type" style="width: 100%">
                <ElOption
                  v-for="item in typeOptions"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </ElSelect>
            </ElFormItem>
          </ElCol>
        </ElRow>

        <ElFormItem label="权限码" prop="PermissionCode">
          <ElInput
            v-model="form.PermissionCode"
            placeholder="按钮类型必填，其他类型可空"
          />
        </ElFormItem>

        <ElRow :gutter="16">
          <ElCol :span="12">
            <ElFormItem label="是否启用" prop="IsEnabled">
              <ElSwitch v-model="form.IsEnabled" />
            </ElFormItem>
          </ElCol>
          <ElCol :span="12">
            <ElFormItem label="是否可见" prop="IsVisible">
              <ElSwitch v-model="form.IsVisible" />
            </ElFormItem>
          </ElCol>
        </ElRow>
      </ElForm>

      <template #footer>
        <ElButton @click="dialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="submitForm">
          保存
        </ElButton>
      </template>
    </ElDialog>

    <RoleMenuDrawer v-model="roleMenuVisible" />
  </div>
</template>
