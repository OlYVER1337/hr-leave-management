import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

type ActivityPoint = {
  month: string;
  annual: number;
  sick: number;
};

type StatusPoint = {
  name: string;
  value: number;
  color: string;
};

type DashboardChartsProps = {
  chartData: ActivityPoint[];
  statusData: StatusPoint[];
};

const tooltipStyle = {
  border: '1px solid #e5eaf2',
  borderRadius: 12,
  boxShadow: '0 8px 24px rgba(17, 34, 68, 0.08)',
  fontSize: 12,
};

export function DashboardCharts({ chartData, statusData }: DashboardChartsProps) {
  return (
    <section className="dashboard-grid">
      <article className="panel chart-panel">
        <div className="panel-header">
          <div>
            <h2>Hoạt động nghỉ phép</h2>
            <p>Số yêu cầu trong sáu tháng gần đây</p>
          </div>
          <span className="panel-select">6 tháng gần đây <ChevronDownIcon /></span>
        </div>
        <div className="chart-legend">
          <span><i className="legend-dot annual" /> Nghỉ phép năm</span>
          <span><i className="legend-dot sick" /> Nghỉ ốm</span>
        </div>
        <div className="chart-wrap">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart
              data={chartData}
              barGap={5}
              margin={{ top: 10, right: 8, left: -24, bottom: 0 }}
            >
              <CartesianGrid stroke="#edf0f6" vertical={false} />
              <XAxis
                dataKey="month"
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#7f8ba1', fontSize: 12 }}
                dy={8}
              />
              <YAxis
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#7f8ba1', fontSize: 11 }}
              />
              <Tooltip contentStyle={tooltipStyle} cursor={{ fill: '#f6f8fc' }} />
              <Bar
                dataKey="annual"
                name="Nghỉ phép năm"
                fill="#5578e8"
                radius={[5, 5, 0, 0]}
                maxBarSize={22}
              />
              <Bar
                dataKey="sick"
                name="Nghỉ ốm"
                fill="#b9a6f7"
                radius={[5, 5, 0, 0]}
                maxBarSize={22}
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </article>

      <article className="panel breakdown-panel">
        <div className="panel-header">
          <div>
            <h2>Trạng thái đơn nghỉ</h2>
            <p>Tình hình xử lý hiện tại</p>
          </div>
          <span className="panel-more">•••</span>
        </div>
        <div className="donut-wrap">
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={statusData}
                dataKey="value"
                nameKey="name"
                innerRadius={64}
                outerRadius={83}
                paddingAngle={4}
                stroke="none"
              >
                {statusData.map((item) => (
                  <Cell key={item.name} fill={item.color} />
                ))}
              </Pie>
              <Tooltip contentStyle={tooltipStyle} />
            </PieChart>
          </ResponsiveContainer>
          <div className="donut-center">
            <strong>{statusData.reduce((sum, item) => sum + item.value, 0)}</strong>
            <span>Tổng số đơn</span>
          </div>
        </div>
        <div className="breakdown-legend">
          {statusData.map((item) => (
            <div key={item.name}>
              <span><i style={{ background: item.color }} />{item.name}</span>
              <strong>{item.value}</strong>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}

function ChevronDownIcon() {
  return <span aria-hidden="true">⌄</span>;
}
